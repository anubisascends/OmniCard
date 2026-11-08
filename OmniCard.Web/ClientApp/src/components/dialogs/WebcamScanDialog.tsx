import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { centroid, quadArea, sigDistance, type DetectedCard, type Point } from '../../utils/cardDetect';
import { cardWidthPx, cropRegion, readRegions } from '../../utils/cardCapture';
import { detectCards, ensureCvReady } from '../../utils/cvWorker';

// --- Tuning constants for the detect → hold steady → capture loop. ---
const DETECT_INTERVAL_MS = 30; // pause between detections; the worker's own detect time sets the real pace.
const DETECT_WIDTH = 720; // downscale frames for detection (fast + small worker transfer); capture is full-res.
const STABLE_MS = 350; // new card(s) must hold still this long before auto-capture...
const STABLE_DRIFT_FRAC = 0.025; // ...staying within 2.5% of frame width of where they settled...
const STABLE_AREA_FRAC = 0.06; // ...and within 6% of their settled size.
const SHARP_KEEP = 0.75; // capture only when focus is ≥ 75% of the best seen while holding still...
const SHARP_WAIT_MS = 1000; // ...unless it never gets back there; then take what we have.
const SAME_CARD_MOVE_FRAC = 0.08; // a captured card nudged < 8% of frame width is still that card...
const SAME_CARD_AREA_FRAC = 0.3;
const SAME_CARD_SIG_BITS = 14; // ...if its content signature differs in ≤ 14 of 64 bits (a new card differs in ~30).
const FORGET_MS = 500; // a captured card unseen this long has been taken away (the same card can then be re-scanned).
const SMALL_CARD_PX = 500; // a card narrower than this in the full-res frame gets the "move closer" hint.
const MAX_THUMBS = 8;
const MULTI_KEY = 'omnicard.webcam.multi';

type Status = 'starting' | 'searching' | 'stabilizing' | 'captured' | 'error';

interface Pose {
  c: Point;
  area: number;
}
/** A card already captured and still sitting in view — not captured again until it leaves or changes.
 *  `sig` is the content signature AT CAPTURE and never follows later frames: if it did, a new card slid
 *  over the old one would drag it along a few bits per frame and be taken for the captured card. */
interface Tracked extends Pose {
  sig: number[];
  lastSeen: number;
}

function readMulti(): boolean {
  try {
    return localStorage.getItem(MULTI_KEY) === 'true';
  } catch {
    return false;
  }
}

function saveMulti(on: boolean) {
  try {
    localStorage.setItem(MULTI_KEY, String(on));
  } catch {
    // Per-browser convenience only.
  }
}

/**
 * Live-webcam card capture. Opens the selected camera, finds cards via opencv.js contour detection
 * (workers/opencv.worker.ts), and when new card(s) hold still and in focus it deskews each into the
 * same layout a dedicated ADF scanner produces (utils/cardDetect.scanLayoutFor) and hands it to
 * `onCapture`. A captured card is remembered by position + content, so it isn't captured again while
 * it sits there, but a different card put in its place is. "Multiple cards" captures every card in
 * view, each as its own scan. "Capture now" forces a capture of everything in view.
 */
export function WebcamScanDialog({
  open,
  game,
  onCapture,
  onClose,
}: {
  open: boolean;
  /** The scan session's game — sets the card size of the ADF layout. */
  game: string;
  onCapture: (file: File) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const overlayRef = useRef<HTMLCanvasElement | null>(null);
  const workRef = useRef<HTMLCanvasElement | null>(null);
  const captureCanvasRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);

  // Loop/state kept in refs so the detection loop never re-renders per frame.
  const runningRef = useRef(false);
  const trackedRef = useRef<Tracked[]>([]);
  const holdRef = useRef<{ poses: Pose[]; since: number; maxSharp: number } | null>(null);
  const lastCardsRef = useRef<DetectedCard[]>([]);
  const captureSeqRef = useRef(0);
  const thumbUrlsRef = useRef<string[]>([]);
  const onCaptureRef = useRef(onCapture);
  onCaptureRef.current = onCapture;
  const gameRef = useRef(game);
  gameRef.current = game;

  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [deviceId, setDeviceId] = useState<string>('');
  const [status, setStatus] = useState<Status>('starting');
  const [error, setError] = useState<string | null>(null);
  const [cvReady, setCvReady] = useState(false);
  const [hasCard, setHasCard] = useState(false);
  const [tooSmall, setTooSmall] = useState(false);
  const [capturedCount, setCapturedCount] = useState(0);
  const [thumbs, setThumbs] = useState<string[]>([]);
  const [flash, setFlash] = useState(false);
  const [multi, setMulti] = useState(readMulti);
  const multiRef = useRef(multi);
  multiRef.current = multi;

  const drawOverlay = useCallback((cards: DetectedCard[], known: Set<DetectedCard>) => {
    const overlay = overlayRef.current;
    const work = workRef.current;
    if (!overlay || !work) return;
    if (overlay.width !== work.width || overlay.height !== work.height) {
      overlay.width = work.width;
      overlay.height = work.height;
    }
    const ctx = overlay.getContext('2d');
    if (!ctx) return;
    ctx.clearRect(0, 0, overlay.width, overlay.height);
    ctx.lineWidth = Math.max(3, overlay.width / 240);
    for (const card of cards) {
      const quad = card.pts;
      ctx.strokeStyle = known.has(card) ? '#4caf50' : '#ffb300';
      ctx.beginPath();
      ctx.moveTo(quad[0].x, quad[0].y);
      for (let i = 1; i < quad.length; i++) ctx.lineTo(quad[i].x, quad[i].y);
      ctx.closePath();
      ctx.stroke();
    }
  }, []);

  const emit = useCallback((blob: Blob) => {
    onCaptureRef.current(new File([blob], `webcam-${Date.now()}-${captureSeqRef.current++}.jpg`, { type: 'image/jpeg' }));
    const url = URL.createObjectURL(blob);
    thumbUrlsRef.current = [url, ...thumbUrlsRef.current];
    // Revoke thumbnails beyond the visible strip.
    while (thumbUrlsRef.current.length > MAX_THUMBS) {
      const old = thumbUrlsRef.current.pop();
      if (old) URL.revokeObjectURL(old);
    }
    setThumbs([...thumbUrlsRef.current]);
    setCapturedCount((n) => n + 1);
  }, []);

  /** Capture `cards` (work-canvas coordinates) from the current video frame at full sensor resolution.
   *  The pixels are read synchronously; the crops then run in the worker while detection carries on. */
  const capture = useCallback(
    (cards: DetectedCard[]) => {
      const video = videoRef.current;
      const work = workRef.current;
      if (!video || !work || video.videoWidth === 0 || cards.length === 0) return;
      const vw = video.videoWidth;
      const vh = video.videoHeight;
      const scale = vw / work.width;
      const cap = (captureCanvasRef.current ??= document.createElement('canvas'));
      if (cap.width !== vw || cap.height !== vh) {
        cap.width = vw;
        cap.height = vh;
      }
      const cctx = cap.getContext('2d', { willReadFrequently: true });
      if (!cctx) {
        setError(t('dialogs.webcam.errors.canvasUnavailable'));
        return;
      }
      cctx.drawImage(video, 0, 0, vw, vh);
      const regions = readRegions(cap, cards.map((card) => card.pts.map((p) => ({ x: p.x * scale, y: p.y * scale }))));

      // Remember them now so the frames that follow don't capture them again while the crops run. A card
      // captured again ("Capture now" on a card already taken) replaces its old entry rather than adding a
      // second one for the same spot.
      const now = performance.now();
      for (const card of cards) {
        const c = centroid(card.pts);
        trackedRef.current = trackedRef.current.filter(
          (tr) => Math.hypot(c.x - tr.c.x, c.y - tr.c.y) / work.width >= SAME_CARD_MOVE_FRAC,
        );
        trackedRef.current.push({ c, area: quadArea(card.pts), sig: card.sig, lastSeen: now });
      }
      setStatus('captured');
      setFlash(true);
      setTimeout(() => setFlash(false), 220);

      void (async () => {
        for (const region of regions) {
          try {
            emit(await cropRegion(region, gameRef.current));
          } catch (e) {
            // A single failed crop shouldn't kill the loop — surface it but keep scanning.
            setError((e as Error).message);
          }
        }
      })();
    },
    [emit, t],
  );

  // One detection pass. The loop (setTimeout-after-completion) never overlaps passes, and the cadence
  // naturally backs off when a frame takes longer to process.
  const detectOnce = useCallback(async () => {
    const video = videoRef.current;
    const work = workRef.current;
    if (!video || !work || video.readyState < 2 || video.videoWidth === 0) return;

    // Detect on a downscaled copy — cheaper CV and a smaller buffer to ship to the worker.
    const dw = Math.min(DETECT_WIDTH, video.videoWidth);
    const dh = Math.round((video.videoHeight * dw) / video.videoWidth);
    if (work.width !== dw || work.height !== dh) {
      work.width = dw;
      work.height = dh;
    }
    const wctx = work.getContext('2d', { willReadFrequently: true });
    if (!wctx) return;
    wctx.drawImage(video, 0, 0, dw, dh);
    const imgData = wctx.getImageData(0, 0, dw, dh);

    let cards: DetectedCard[] = [];
    try {
      cards = await detectCards(imgData, multiRef.current);
    } catch {
      cards = [];
    }
    const now = performance.now();
    const frameW = work.width;

    // Split what's in view into cards already captured (same spot, same content) and new ones.
    const known = new Set<DetectedCard>();
    const claimed = new Set<Tracked>();
    for (const card of cards) {
      const c = centroid(card.pts);
      const area = quadArea(card.pts);
      const hit = trackedRef.current.find(
        (tr) =>
          !claimed.has(tr) &&
          Math.hypot(c.x - tr.c.x, c.y - tr.c.y) / frameW < SAME_CARD_MOVE_FRAC &&
          Math.abs(area - tr.area) / tr.area < SAME_CARD_AREA_FRAC &&
          sigDistance(card.sig, tr.sig) <= SAME_CARD_SIG_BITS,
      );
      if (hit) {
        claimed.add(hit);
        // Follow the card's position, but keep the signature it was captured with (see Tracked).
        Object.assign(hit, { c, area, lastSeen: now });
        known.add(card);
      }
    }
    trackedRef.current = trackedRef.current.filter((tr) => now - tr.lastSeen <= FORGET_MS);
    const fresh = cards.filter((card) => !known.has(card));

    lastCardsRef.current = cards;
    drawOverlay(cards, known);
    setHasCard(cards.length > 0);
    const scale = video.videoWidth / frameW;
    setTooSmall(cards.some((card) => cardWidthPx(card.pts) * scale < SMALL_CARD_PX));

    if (fresh.length === 0) {
      holdRef.current = null;
      setStatus(cards.length > 0 ? 'captured' : 'searching');
      return;
    }

    // New card(s): wait until they hold still where they settled, then catch the sharpest moment.
    setStatus('stabilizing');
    const poses = fresh.map((card) => ({ c: centroid(card.pts), area: quadArea(card.pts) }));
    const sharp = fresh.reduce((s, card) => s + card.sharpness, 0);
    const hold = holdRef.current;
    const steady =
      hold !== null &&
      hold.poses.length === poses.length &&
      poses.every((p) =>
        hold.poses.some(
          (h) =>
            Math.hypot(p.c.x - h.c.x, p.c.y - h.c.y) / frameW < STABLE_DRIFT_FRAC &&
            Math.abs(p.area - h.area) / h.area < STABLE_AREA_FRAC,
        ),
      );
    if (!steady) {
      holdRef.current = { poses, since: now, maxSharp: sharp };
      return;
    }
    hold.maxSharp = Math.max(hold.maxSharp, sharp);
    const held = now - hold.since;
    if (held >= STABLE_MS && (sharp >= SHARP_KEEP * hold.maxSharp || held >= SHARP_WAIT_MS)) {
      holdRef.current = null;
      capture(fresh);
    }
  }, [drawOverlay, capture]);

  // Manual capture — everything in view, including cards already captured.
  const captureNow = useCallback(() => {
    holdRef.current = null;
    capture(lastCardsRef.current);
  }, [capture]);

  // Start (or restart) the camera stream for the chosen device.
  const startStream = useCallback(async (preferredDeviceId?: string) => {
    // Tear down any existing stream first.
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    setError(null);
    setStatus('starting');
    try {
      // Ask for up to 4K: the capture is cropped from the full frame, so sensor pixels are what the
      // fine print (collector number, set code) is read from. Cameras that can't do it pick their best.
      const size = { width: { ideal: 3840 }, height: { ideal: 2160 } };
      const constraints: MediaStreamConstraints = {
        video: preferredDeviceId
          ? { deviceId: { exact: preferredDeviceId }, ...size }
          : { facingMode: 'environment', ...size },
        audio: false,
      };
      const stream = await navigator.mediaDevices.getUserMedia(constraints);
      streamRef.current = stream;
      // Keep autofocus running where the camera exposes it (not in the standard typings).
      const track = stream.getVideoTracks()[0];
      await track
        ?.applyConstraints({ advanced: [{ focusMode: 'continuous' } as MediaTrackConstraintSet] })
        .catch(() => undefined);
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => undefined);
      }
      // Labels are only populated after permission is granted — (re)enumerate now.
      const all = await navigator.mediaDevices.enumerateDevices();
      const cams = all.filter((d) => d.kind === 'videoinput');
      setDevices(cams);
      const active = track?.getSettings().deviceId;
      if (active) setDeviceId(active);
      setStatus('searching');
    } catch (e) {
      const err = e as DOMException;
      const msg =
        err.name === 'NotAllowedError'
          ? t('dialogs.webcam.errors.permissionDenied')
          : err.name === 'NotFoundError'
            ? t('dialogs.webcam.errors.notFound')
            : t('dialogs.webcam.errors.startFailed', { message: err.message });
      setError(msg);
      setStatus('error');
    }
  }, [t]);

  // Lifecycle: on open, warm up opencv and start the camera + loop; on close, tear everything down.
  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    // Reset per-session state.
    trackedRef.current = [];
    holdRef.current = null;
    lastCardsRef.current = [];
    setCapturedCount(0);
    setThumbs([]);
    setHasCard(false);
    setTooSmall(false);

    ensureCvReady()
      .then(() => {
        if (!cancelled) setCvReady(true);
      })
      .catch(() => {
        if (!cancelled) setError(t('dialogs.webcam.errors.engineLoadFailed'));
      });

    void startStream();

    runningRef.current = true;
    const tick = async () => {
      if (!runningRef.current) return;
      try {
        await detectOnce();
      } catch {
        // One bad frame (or a capture that threw) must never stop the loop — it would silently stop
        // capturing for the rest of the session.
      }
      if (runningRef.current) setTimeout(tick, DETECT_INTERVAL_MS);
    };
    void tick();

    return () => {
      cancelled = true;
      runningRef.current = false;
      streamRef.current?.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
      thumbUrlsRef.current.forEach((u) => URL.revokeObjectURL(u));
      thumbUrlsRef.current = [];
    };
    // detectOnce/startStream are stable (useCallback); we intentionally run this once per open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const toggleMulti = (on: boolean) => {
    setMulti(on);
    saveMulti(on);
    trackedRef.current = [];
    holdRef.current = null;
  };

  const statusText: Record<Status, string> = {
    starting: t('dialogs.webcam.status.starting'),
    searching: multi ? t('dialogs.webcam.status.searchingMulti') : t('dialogs.webcam.status.searching'),
    stabilizing: t('dialogs.webcam.status.stabilizing'),
    captured: multi ? t('dialogs.webcam.status.capturedMulti') : t('dialogs.webcam.status.captured'),
    error: t('dialogs.webcam.status.error'),
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('dialogs.webcam.title')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error && <Alert severity="error">{error}</Alert>}

          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            {devices.length > 1 && (
              <TextField
                select
                size="small"
                label={t('dialogs.webcam.camera')}
                value={deviceId}
                onChange={(e) => {
                  setDeviceId(e.target.value);
                  void startStream(e.target.value);
                }}
                sx={{ flex: 1, minWidth: 200 }}
              >
                {devices.map((d, i) => (
                  <MenuItem key={d.deviceId} value={d.deviceId}>
                    {d.label || t('dialogs.webcam.cameraN', { number: i + 1 })}
                  </MenuItem>
                ))}
              </TextField>
            )}
            <FormControlLabel
              control={<Switch checked={multi} onChange={(e) => toggleMulti(e.target.checked)} />}
              label={t('dialogs.webcam.multiple')}
            />
          </Stack>
          {multi && (
            <Typography variant="body2" color="text.secondary">
              {t('dialogs.webcam.multipleHint')}
            </Typography>
          )}

          <Box
            sx={{
              position: 'relative',
              width: '100%',
              backgroundColor: 'black',
              borderRadius: 1,
              overflow: 'hidden',
              aspectRatio: '4 / 3',
              outline: flash ? '4px solid #4caf50' : 'none',
              transition: 'outline 0.1s',
            }}
          >
            <video
              ref={videoRef}
              playsInline
              muted
              style={{ width: '100%', height: '100%', objectFit: 'contain', display: 'block' }}
            />
            <canvas
              ref={overlayRef}
              style={{
                position: 'absolute',
                inset: 0,
                width: '100%',
                height: '100%',
                objectFit: 'contain',
                pointerEvents: 'none',
              }}
            />
            {(!cvReady || status === 'starting') && !error && (
              <Box
                sx={{
                  position: 'absolute',
                  inset: 0,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'white',
                  gap: 1,
                }}
              >
                <CircularProgress size={20} color="inherit" />
                <Typography variant="body2">{cvReady ? t('dialogs.webcam.status.starting') : t('dialogs.webcam.loadingEngine')}</Typography>
              </Box>
            )}
          </Box>
          {/* Offscreen canvas the detection loop draws frames into. */}
          <canvas ref={workRef} style={{ display: 'none' }} />

          <Stack direction="row" spacing={1} alignItems="center" justifyContent="space-between">
            <Typography
              variant="body2"
              color={status === 'captured' ? 'success.main' : 'text.secondary'}
              sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}
            >
              {status === 'captured' && <CheckCircleIcon fontSize="small" color="success" />}
              {statusText[status]}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t('dialogs.webcam.capturedCount', { count: capturedCount })}
            </Typography>
          </Stack>
          {tooSmall && (
            <Typography variant="body2" color="warning.main">
              {t('dialogs.webcam.tooSmall')}
            </Typography>
          )}

          {thumbs.length > 0 && (
            <Stack direction="row" spacing={1} sx={{ overflowX: 'auto' }}>
              {thumbs.map((src, i) => (
                <Box
                  key={src}
                  component="img"
                  src={src}
                  alt={t('dialogs.webcam.captureAlt', { number: capturedCount - i })}
                  sx={{ height: 72, borderRadius: 1, border: '1px solid', borderColor: 'divider' }}
                />
              ))}
            </Stack>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button
          onClick={captureNow}
          startIcon={<CameraAltIcon />}
          disabled={!hasCard || !cvReady || !!error}
        >
          {t('dialogs.webcam.captureNow')}
        </Button>
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose} variant="contained">
          {t('common.actions.done')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
