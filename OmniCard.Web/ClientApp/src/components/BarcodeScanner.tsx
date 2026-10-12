import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, Box, IconButton, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material';
import FlashlightOnIcon from '@mui/icons-material/FlashlightOn';
import FlashlightOffIcon from '@mui/icons-material/FlashlightOff';
import { BrowserMultiFormatReader, type IScannerControls } from '@zxing/browser';
import { BarcodeFormat, DecodeHintType } from '@zxing/library';
import { EnhancedBarcodeDecoder, GUIDE } from './barcodeEnhance';

const CAMERA_KEY = 'omnicard.barcode.camera';
/** How often the sharpened 1D pass runs alongside zxing's own (raw-frame) decode loop. */
const ENHANCED_INTERVAL_MS = 250;

// Shipping labels use 1D Code 128 (USPS/UPS/FedEx), plus PDF417 / Data Matrix / QR 2D blocks on some
// carriers; the rest cover packing slips and the odd product label.
const FORMATS = [
  BarcodeFormat.CODE_128,
  BarcodeFormat.CODE_39,
  BarcodeFormat.CODE_93,
  BarcodeFormat.ITF,
  BarcodeFormat.CODABAR,
  BarcodeFormat.PDF_417,
  BarcodeFormat.DATA_MATRIX,
  BarcodeFormat.QR_CODE,
];

function readCamera(): string | undefined {
  try {
    return localStorage.getItem(CAMERA_KEY) ?? undefined;
  } catch {
    return undefined;
  }
}

function saveCamera(id: string) {
  try {
    localStorage.setItem(CAMERA_KEY, id);
  } catch {
    // Per-browser convenience only.
  }
}

/**
 * Live camera barcode reader (phone rear camera or a desk webcam). Calls `onCode` once per distinct
 * barcode — the same code is ignored for `repeatMs` so a label held in view doesn't fire repeatedly.
 * While `paused`, decoded codes are dropped but the camera keeps running (no re-permission prompt).
 * The chosen camera is remembered per browser. Besides zxing's raw-frame loop, a sharpened pass over the
 * aiming band (see `EnhancedBarcodeDecoder`) reads the 1D barcodes a soft webcam image would miss.
 */
export function BarcodeScanner({
  onCode,
  paused = false,
  repeatMs = 4000,
  height = 320,
}: {
  onCode: (code: string) => void;
  paused?: boolean;
  repeatMs?: number;
  height?: number;
}) {
  const { t } = useTranslation();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const controlsRef = useRef<IScannerControls | null>(null);
  const lastRef = useRef<{ code: string; at: number } | null>(null);
  const onCodeRef = useRef(onCode);
  const pausedRef = useRef(paused);
  onCodeRef.current = onCode;
  pausedRef.current = paused;

  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [deviceId, setDeviceId] = useState<string | undefined>(readCamera);
  const [activeId, setActiveId] = useState<string | undefined>(); // the camera actually streaming
  const [error, setError] = useState<string | null>(null);
  const [torch, setTorch] = useState<boolean | null>(null); // null = torch not supported

  const handleResult = useCallback(
    (text: string) => {
      if (pausedRef.current) return;
      const now = Date.now();
      const last = lastRef.current;
      if (last && last.code === text && now - last.at < repeatMs) {
        last.at = now; // still in view — keep suppressing until it leaves for repeatMs
        return;
      }
      lastRef.current = { code: text, at: now };
      onCodeRef.current(text);
    },
    [repeatMs],
  );

  useEffect(() => {
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      setError(t('sales.ship.camera.insecure'));
      return;
    }
    let cancelled = false;
    let enhancedTimer: number | undefined;
    const hints = new Map<DecodeHintType, unknown>([
      [DecodeHintType.POSSIBLE_FORMATS, FORMATS],
      [DecodeHintType.TRY_HARDER, true],
    ]);
    const reader = new BrowserMultiFormatReader(hints, { delayBetweenScanAttempts: 120, delayBetweenScanSuccess: 300 });
    setError(null);
    setTorch(null);

    const video: MediaTrackConstraints = deviceId
      ? { deviceId: { exact: deviceId }, width: { ideal: 1920 }, height: { ideal: 1080 } }
      : { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } };

    reader
      .decodeFromConstraints({ video, audio: false }, videoRef.current ?? undefined, (result) => {
        if (result) handleResult(result.getText());
      })
      .then(async (controls) => {
        if (cancelled) {
          controls.stop();
          return;
        }
        controlsRef.current = controls;
        if (controls.switchTorch) setTorch(false);
        const stream = videoRef.current?.srcObject as MediaStream | null;
        const track = stream?.getVideoTracks()[0];
        setActiveId(track?.getSettings().deviceId);
        // Webcams that can refocus often start fixed — a close-up label then never gets sharp.
        const focusModes = (track?.getCapabilities?.() as { focusMode?: string[] } | undefined)?.focusMode;
        if (track && focusModes?.includes('continuous')) {
          track.applyConstraints({ advanced: [{ focusMode: 'continuous' } as MediaTrackConstraintSet] }).catch(() => {});
        }
        const enhanced = new EnhancedBarcodeDecoder();
        enhancedTimer = window.setInterval(() => {
          if (pausedRef.current || !videoRef.current) return;
          const text = enhanced.decode(videoRef.current);
          if (text) handleResult(text);
        }, ENHANCED_INTERVAL_MS);
        // Device labels are only available after permission is granted.
        const cams = await BrowserMultiFormatReader.listVideoInputDevices();
        if (!cancelled) setDevices(cams);
      })
      .catch((e: DOMException) => {
        if (cancelled) return;
        if (deviceId && e.name === 'OverconstrainedError') {
          // The remembered camera is gone (unplugged webcam) — fall back to the default one.
          setDeviceId(undefined);
          return;
        }
        setError(
          e.name === 'NotAllowedError'
            ? t('dialogs.webcam.errors.permissionDenied')
            : e.name === 'NotFoundError'
              ? t('dialogs.webcam.errors.notFound')
              : t('dialogs.webcam.errors.startFailed', { message: e.message }),
        );
      });

    return () => {
      cancelled = true;
      window.clearInterval(enhancedTimer);
      controlsRef.current?.stop();
      controlsRef.current = null;
    };
  }, [deviceId, handleResult, t]);

  const toggleTorch = async () => {
    const next = !torch;
    try {
      await controlsRef.current?.switchTorch?.(next);
      setTorch(next);
    } catch {
      setTorch(null);
    }
  };

  if (error) return <Alert severity="warning">{error}</Alert>;

  return (
    <Stack spacing={1}>
      <Box
        sx={{
          position: 'relative',
          width: '100%',
          height,
          bgcolor: 'black',
          borderRadius: 1,
          overflow: 'hidden',
          opacity: paused ? 0.5 : 1,
        }}
      >
        <Box component="video" ref={videoRef} muted playsInline sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
        {/* Aiming guide — a wide box suits the 1D barcodes on shipping labels. */}
        <Box
          sx={{
            position: 'absolute',
            left: `${GUIDE.left * 100}%`,
            right: `${GUIDE.right * 100}%`,
            top: `${GUIDE.top * 100}%`,
            bottom: `${GUIDE.bottom * 100}%`,
            border: '2px solid rgba(255,255,255,0.8)',
            borderRadius: 1,
            boxShadow: '0 0 0 9999px rgba(0,0,0,0.25)',
            pointerEvents: 'none',
          }}
        />
        {torch !== null && (
          <Tooltip title={t('sales.ship.camera.torch')}>
            <IconButton
              onClick={toggleTorch}
              sx={{ position: 'absolute', right: 8, bottom: 8, color: 'white', bgcolor: 'rgba(0,0,0,0.4)' }}
            >
              {torch ? <FlashlightOffIcon /> : <FlashlightOnIcon />}
            </IconButton>
          </Tooltip>
        )}
      </Box>
      <Typography variant="caption" color="text.secondary">
        {t('sales.ship.camera.hint')}
      </Typography>
      {devices.length > 1 && (
        <TextField
          select
          size="small"
          label={t('sales.ship.camera.device')}
          value={devices.some((d) => d.deviceId === activeId) ? activeId : ''}
          onChange={(e) => {
            setDeviceId(e.target.value);
            saveCamera(e.target.value);
          }}
        >
          {devices.map((d, i) => (
            <MenuItem key={d.deviceId} value={d.deviceId}>
              {d.label || t('sales.ship.camera.unnamed', { n: i + 1 })}
            </MenuItem>
          ))}
        </TextField>
      )}
    </Stack>
  );
}
