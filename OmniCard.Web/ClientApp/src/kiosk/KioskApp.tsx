import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Box, Button, CircularProgress, Typography } from '@mui/material';
import StorefrontIcon from '@mui/icons-material/Storefront';
import { useTranslation } from 'react-i18next';
import type { KioskSessionDto, KioskStatusDto, KioskSubmitResultDto } from '../api/types';
import { kioskApi } from './kioskApi';
import { EnrollScreen, ExitKioskDialog } from './AdminScreens';
import { WelcomeScreen } from './WelcomeScreen';
import { BrowseScreen } from './BrowseScreen';
import { Centered } from './Centered';

export const STATUS_KEY = ['kiosk-status'];

/** Taps on the corner logo within this window that open the (admin-only) "Exit kiosk mode" dialog. */
const EXIT_TAPS = 5;
const EXIT_TAP_WINDOW_MS = 3000;

/**
 * The customer kiosk's screens: enroll (admins only, once per device) → welcome (customer types a name)
 * → browse (search, hold cards, send the list) → "sent to the counter" → welcome again. The customer's
 * session lives on the server, keyed to this device, so a reload picks up where they were.
 */
export function KioskApp() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const status = useQuery({ queryKey: STATUS_KEY, queryFn: kioskApi.status });
  const [submitted, setSubmitted] = useState<KioskSubmitResultDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [exitOpen, setExitOpen] = useState(false);
  const taps = useRef<number[]>([]);

  const setSession = (session: KioskSessionDto | null) =>
    qc.setQueryData<KioskStatusDto>(STATUS_KEY, (old) => (old ? { ...old, session } : old));

  /** The server ended the session (idle timeout, revoked device…): back to the welcome screen. */
  const sessionLost = () => {
    setNotice(t('kiosk.app.browse.sessionEnded'));
    void qc.invalidateQueries({ queryKey: STATUS_KEY });
  };

  const onLogoTap = () => {
    const now = Date.now();
    taps.current = [...taps.current.filter((at) => now - at < EXIT_TAP_WINDOW_MS), now];
    if (taps.current.length >= EXIT_TAPS) {
      taps.current = [];
      setExitOpen(true);
    }
  };

  if (status.isLoading) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', height: '100vh' }}>
        <CircularProgress />
      </Box>
    );
  }
  if (status.isError || !status.data) {
    return (
      <Centered>
        <Typography variant="h5">{t('kiosk.app.error')}</Typography>
        <Button variant="contained" size="large" onClick={() => void status.refetch()}>
          {t('common.actions.retry')}
        </Button>
      </Centered>
    );
  }

  const s = status.data;
  const logo = (
    <Box
      onClick={onLogoTap}
      sx={{ display: 'flex', alignItems: 'center', gap: 1, cursor: 'default', userSelect: 'none' }}
      aria-hidden
    >
      <StorefrontIcon color="primary" />
      <Typography variant="h6" component="span">
        {t('kiosk.app.title')}
      </Typography>
    </Box>
  );
  const exitDialog = (
    <ExitKioskDialog
      open={exitOpen}
      onClose={() => setExitOpen(false)}
      onExited={() => {
        setExitOpen(false);
        void qc.invalidateQueries({ queryKey: STATUS_KEY });
      }}
    />
  );

  let screen;
  if (!s.enrolled) {
    screen = <EnrollScreen onEnrolled={(next) => qc.setQueryData(STATUS_KEY, next)} />;
  } else if (!s.enabled || !s.hasLocations) {
    screen = (
      <Centered>
        <Typography variant="h4">{t('kiosk.app.closed.title')}</Typography>
        <Typography color="text.secondary">
          {s.enabled ? t('kiosk.app.noLocations') : t('kiosk.app.closed.body')}
        </Typography>
        <Button onClick={() => void status.refetch()}>{t('common.actions.refresh')}</Button>
      </Centered>
    );
  } else if (submitted) {
    screen = <SubmittedScreen result={submitted} onDone={() => setSubmitted(null)} />;
  } else if (s.session) {
    screen = (
      <BrowseScreen
        status={s}
        session={s.session}
        logo={logo}
        onSession={setSession}
        onSessionLost={sessionLost}
        onSubmitted={(result) => {
          setSubmitted(result);
          setSession(null);
        }}
        onLeave={() => setSession(null)}
      />
    );
  } else {
    screen = (
      <WelcomeScreen
        status={s}
        logo={logo}
        notice={notice}
        onStarted={(session) => {
          setNotice(null);
          setSession(session);
        }}
      />
    );
  }

  return (
    <>
      {screen}
      {s.enrolled && exitDialog}
    </>
  );
}

/** "Your list is on its way" — returns to the welcome screen by itself after a while. */
function SubmittedScreen({ result, onDone }: { result: KioskSubmitResultDto; onDone: () => void }) {
  const { t } = useTranslation();
  useAutoReturn(onDone, 20_000);
  return (
    <Centered>
      <Typography variant="h3">{t('kiosk.app.submitted.title', { name: result.customerName })}</Typography>
      <Typography variant="h6" color="text.secondary" sx={{ maxWidth: 560 }}>
        {t('kiosk.app.submitted.body', { count: result.itemCount })}
      </Typography>
      <Button variant="contained" size="large" onClick={onDone}>
        {t('kiosk.app.submitted.done')}
      </Button>
    </Centered>
  );
}

function useAutoReturn(onDone: () => void, ms: number) {
  const done = useRef(onDone);
  done.current = onDone;
  useEffect(() => {
    const timer = window.setTimeout(() => done.current(), ms);
    return () => window.clearTimeout(timer);
  }, [ms]);
}
