import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, Box, IconButton, MenuItem, Stack, TextField, Tooltip } from '@mui/material';
import FlashlightOnIcon from '@mui/icons-material/FlashlightOn';
import FlashlightOffIcon from '@mui/icons-material/FlashlightOff';
import { BrowserMultiFormatReader, type IScannerControls } from '@zxing/browser';
import { BarcodeFormat, DecodeHintType } from '@zxing/library';

const CAMERA_KEY = 'omnicard.barcode.camera';

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
 * The chosen camera is remembered per browser.
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
        setActiveId(stream?.getVideoTracks()[0]?.getSettings().deviceId);
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
            left: '8%',
            right: '8%',
            top: '30%',
            bottom: '30%',
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
