import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { centroid, quadArea, type Point } from '../../utils/cardDetect';
import { cropCard, detectCardQuad, ensureCvReady } from '../../utils/cvWorker';

// --- Tuning constants for the detect → stabilize → capture → lock state machine. ---
const DETECT_INTERVAL_MS = 120; // ~8 fps detection cadence.
const DETECT_WIDTH = 720; // downscale frames for detection (fast + small worker transfer); capture is full-res.
const STABLE_FRAMES = 8; // consecutive stable frames (~0.8s) before auto-capture.
const MISSING_TO_REARM = 5; // frames with no card before we re-arm for the next one.
const STABLE_MOVE_FRAC = 0.02; // centroid may drift < 2% of frame width and still count as "stable".
const STABLE_AREA_FRAC = 0.05; // quad area may change < 5% and still count as "stable".
const REARM_MOVE_FRAC = 0.08; // a locked card that jumps > 8% is treated as a new card.
const REARM_AREA_FRAC = 0.25; // ...or whose area changes > 25%.
const MAX_THUMBS = 5;

type Phase = 'armed' | 'locked';
type Status = 'starting' | 'searching' | 'stabilizing' | 'captured' | 'error';

/**
 * Live-webcam card capture. Opens the selected camera, auto-detects a card via opencv.js contour
 * detection (see utils/cardDetect), and when the card holds still it deskews + crops it to a thin-border
 * portrait JPEG and hands it to `onCapture`. A card left sitting in view is captured only once (the
 * machine locks until the card leaves or changes); the "Capture now" button forces an extra scan of the
 * same card. The dialog stays open so a stack of cards can be scanned in a row.
 */
export function WebcamScanDialog({
  open,
  onCapture,
  onClose,
}: {
  open: boolean;
  onCapture: (file: File) => void;
  onClose: () => void;
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const overlayRef = useRef<HTMLCanvasElement | null>(null);
  const workRef = useRef<HTMLCanvasElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);

  // Loop/state kept in refs so the detection loop never re-renders per frame.
  const runningRef = useRef(false);
  const phaseRef = useRef<Phase>('armed');
  const stableCountRef = useRef(0);
  const missingCountRef = useRef(0);
  const lastRef = useRef<{ c: Point; area: number } | null>(null);
  const lastQuadRef = useRef<Point[] | null>(null);
  const busyRef = useRef(false);
  const captureSeqRef = useRef(0);
  const thumbUrlsRef = useRef<string[]>([]);

  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [deviceId, setDeviceId] = useState<string>('');
  const [status, setStatus] = useState<Status>('starting');
  const [error, setError] = useState<string | null>(null);
  const [cvReady, setCvReady] = useState(false);
  const [hasCard, setHasCard] = useState(false);
  const [capturedCount, setCapturedCount] = useState(0);
  const [thumbs, setThumbs] = useState<string[]>([]);
  const [flash, setFlash] = useState(false);

  const drawOverlay = useCallback((quad: Point[] | null, locked: boolean) => {
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
    if (!quad || quad.length !== 4) return;
    ctx.lineWidth = Math.max(3, overlay.width / 240);
    ctx.strokeStyle = locked ? '#4caf50' : '#ffb300';
    ctx.beginPath();
    ctx.moveTo(quad[0].x, quad[0].y);
    for (let i = 1; i < quad.length; i++) ctx.lineTo(quad[i].x, quad[i].y);
    ctx.closePath();
    ctx.stroke();
  }, []);

  const doCapture = useCallback(
    async (quad: Point[]) => {
      const video = videoRef.current;
      const work = workRef.current;
      if (!video || !work || busyRef.current || video.videoWidth === 0) return;
      busyRef.current = true;
      try {
        // Capture at full sensor resolution: re-grab the frame at native size and scale the quad
        // (detected on the downscaled work canvas) up to match, so the crop keeps maximum detail.
        const vw = video.videoWidth;
        const vh = video.videoHeight;
        const scale = vw / work.width;
        const cap = document.createElement('canvas');
        cap.width = vw;
        cap.height = vh;
        const cctx = cap.getContext('2d');
        if (!cctx) throw new Error('Canvas 2D context unavailable');
        cctx.drawImage(video, 0, 0, vw, vh);
        const full = cctx.getImageData(0, 0, vw, vh);
        const scaledQuad = quad.map((p) => ({ x: p.x * scale, y: p.y * scale }));
        const blob = await cropCard(full, scaledQuad);
        const file = new File([blob], `webcam-${Date.now()}-${captureSeqRef.current++}.jpg`, {
          type: 'image/jpeg',
        });
        onCapture(file);

        const url = URL.createObjectURL(blob);
        thumbUrlsRef.current = [url, ...thumbUrlsRef.current];
        // Revoke thumbnails beyond the visible strip.
        while (thumbUrlsRef.current.length > MAX_THUMBS) {
          const old = thumbUrlsRef.current.pop();
          if (old) URL.revokeObjectURL(old);
        }
        setThumbs([...thumbUrlsRef.current]);
        setCapturedCount((n) => n + 1);
        setFlash(true);
        setTimeout(() => setFlash(false), 220);
      } catch (e) {
        // A single failed crop shouldn't kill the loop — surface it but keep scanning.
        setError((e as Error).message);
      } finally {
        busyRef.current = false;
      }
    },
    [onCapture],
  );

  // The self-scheduling detection loop. Uses setTimeout-after-completion so detections never overlap
  // and the cadence naturally backs off if a frame takes longer than DETECT_INTERVAL_MS.
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

    let quad: Point[] | null = null;
    try {
      quad = await detectCardQuad(imgData);
    } catch {
      quad = null;
    }

    const locked = phaseRef.current === 'locked';
    drawOverlay(quad, locked);

    if (!quad) {
      setHasCard(false);
      lastQuadRef.current = null;
      missingCountRef.current++;
      if (missingCountRef.current >= MISSING_TO_REARM) {
        phaseRef.current = 'armed';
        stableCountRef.current = 0;
        lastRef.current = null;
        setStatus('searching');
      }
      return;
    }

    setHasCard(true);
    lastQuadRef.current = quad;
    missingCountRef.current = 0;

    const c = centroid(quad);
    const area = quadArea(quad);
    const frameW = work.width;
    const prev = lastRef.current;
    const moved = prev ? Math.hypot(c.x - prev.c.x, c.y - prev.c.y) / frameW : Infinity;
    const areaDelta = prev ? Math.abs(area - prev.area) / prev.area : Infinity;

    if (phaseRef.current === 'locked') {
      // Detect that a different card has been presented; if so, re-arm for a fresh auto-capture.
      if (moved > REARM_MOVE_FRAC || areaDelta > REARM_AREA_FRAC) {
        phaseRef.current = 'armed';
        stableCountRef.current = 1;
        setStatus('stabilizing');
      }
      lastRef.current = { c, area };
      return;
    }

    // Armed: count consecutive stable frames, then capture.
    if (prev && moved < STABLE_MOVE_FRAC && areaDelta < STABLE_AREA_FRAC) {
      stableCountRef.current++;
    } else {
      stableCountRef.current = 1;
    }
    lastRef.current = { c, area };
    setStatus('stabilizing');

    if (stableCountRef.current >= STABLE_FRAMES && !busyRef.current) {
      phaseRef.current = 'locked';
      setStatus('captured');
      await doCapture(quad);
    }
  }, [drawOverlay, doCapture]);

  // Manual capture — bypasses the lock so the same static card can be re-scanned on demand.
  const captureNow = useCallback(() => {
    const quad = lastQuadRef.current;
    if (quad && !busyRef.current) {
      phaseRef.current = 'locked';
      setStatus('captured');
      void doCapture(quad);
    }
  }, [doCapture]);

  // Start (or restart) the camera stream for the chosen device.
  const startStream = useCallback(async (preferredDeviceId?: string) => {
    // Tear down any existing stream first.
    streamRef.current?.getTracks().forEach((t) => t.stop());
    streamRef.current = null;
    setError(null);
    setStatus('starting');
    try {
      const constraints: MediaStreamConstraints = {
        video: preferredDeviceId
          ? { deviceId: { exact: preferredDeviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } }
          : { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } },
        audio: false,
      };
      const stream = await navigator.mediaDevices.getUserMedia(constraints);
      streamRef.current = stream;
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play().catch(() => undefined);
      }
      // Labels are only populated after permission is granted — (re)enumerate now.
      const all = await navigator.mediaDevices.enumerateDevices();
      const cams = all.filter((d) => d.kind === 'videoinput');
      setDevices(cams);
      const active = stream.getVideoTracks()[0]?.getSettings().deviceId;
      if (active) setDeviceId(active);
      setStatus('searching');
    } catch (e) {
      const err = e as DOMException;
      const msg =
        err.name === 'NotAllowedError'
          ? 'Camera permission was denied. Allow camera access in your browser and try again.'
          : err.name === 'NotFoundError'
            ? 'No camera was found. Connect a webcam and try again.'
            : `Could not start the camera: ${err.message}`;
      setError(msg);
      setStatus('error');
    }
  }, []);

  // Lifecycle: on open, warm up opencv and start the camera + loop; on close, tear everything down.
  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    // Reset per-session state.
    phaseRef.current = 'armed';
    stableCountRef.current = 0;
    missingCountRef.current = 0;
    lastRef.current = null;
    lastQuadRef.current = null;
    setCapturedCount(0);
    setThumbs([]);
    setHasCard(false);

    ensureCvReady()
      .then(() => {
        if (!cancelled) setCvReady(true);
      })
      .catch(() => {
        if (!cancelled) setError('Failed to load the image-processing engine (opencv.js).');
      });

    void startStream();

    runningRef.current = true;
    const tick = async () => {
      if (!runningRef.current) return;
      await detectOnce();
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

  const statusText: Record<Status, string> = {
    starting: 'Starting camera…',
    searching: 'Point a card at the camera',
    stabilizing: 'Hold steady…',
    captured: 'Captured ✓  —  present the next card',
    error: 'Camera unavailable',
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Scan with webcam</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {error && <Alert severity="error">{error}</Alert>}

          {devices.length > 1 && (
            <TextField
              select
              size="small"
              label="Camera"
              value={deviceId}
              onChange={(e) => {
                setDeviceId(e.target.value);
                void startStream(e.target.value);
              }}
            >
              {devices.map((d, i) => (
                <MenuItem key={d.deviceId} value={d.deviceId}>
                  {d.label || `Camera ${i + 1}`}
                </MenuItem>
              ))}
            </TextField>
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
                <Typography variant="body2">{cvReady ? 'Starting camera…' : 'Loading engine…'}</Typography>
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
              Captured: {capturedCount}
            </Typography>
          </Stack>

          {thumbs.length > 0 && (
            <Stack direction="row" spacing={1} sx={{ overflowX: 'auto' }}>
              {thumbs.map((src, i) => (
                <Box
                  key={src}
                  component="img"
                  src={src}
                  alt={`Capture ${capturedCount - i}`}
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
          Capture now
        </Button>
        <Box sx={{ flex: 1 }} />
        <Button onClick={onClose} variant="contained">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
}
