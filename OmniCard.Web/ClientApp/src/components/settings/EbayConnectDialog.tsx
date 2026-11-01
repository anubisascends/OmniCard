import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { api, ApiError } from '../../api/client';

/**
 * Connect to eBay without needing a callback the browser can reach: open eBay's sign-in in a new tab,
 * approve, then paste the address eBay lands on (it carries the authorization code even when that
 * page fails to load). If the RuName's accepted URL does point at this server's callback, the other
 * tab finishes on its own — the dialog polls the status and closes when the connection appears.
 * In broker mode this is only the fallback ("Paste a code instead"), so the callback tip is hidden.
 */
export function EbayConnectDialog({
  open,
  onClose,
  showCallbackTip,
}: {
  open: boolean;
  onClose: (connected: boolean) => void;
  showCallbackTip: boolean;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [input, setInput] = useState('');
  const [opened, setOpened] = useState(false);
  const callbackUrl = `${window.location.origin}/api/ebay/callback`;

  const status = useQuery({
    queryKey: ['ebay-status'],
    queryFn: api.ebayStatus,
    enabled: open && opened,
    refetchInterval: 3000,
  });

  const connect = useMutation({
    mutationFn: () => api.ebayConnectWithCode(input),
    onSuccess: (data) => {
      qc.setQueryData(['ebay-status'], data);
      onClose(true);
    },
  });

  useEffect(() => {
    if (open && opened && status.data?.connected) onClose(true);
  }, [open, opened, status.data?.connected, onClose]);

  useEffect(() => {
    if (!open) {
      setInput('');
      setOpened(false);
      connect.reset();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset only when the dialog closes
  }, [open]);

  const reason = connect.error instanceof ApiError ? (connect.error.body as { reason?: string })?.reason : undefined;

  return (
    <Dialog open={open} onClose={() => onClose(false)} maxWidth="sm" fullWidth>
      <DialogTitle>{t('settings.ebay.connectDialog.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Stack spacing={1}>
            <Typography variant="subtitle2">{t('settings.ebay.connectDialog.step1Title')}</Typography>
            <Typography variant="body2" color="text.secondary">
              {t('settings.ebay.connectDialog.step1Body')}
            </Typography>
            <span>
              <Button
                variant={opened ? 'outlined' : 'contained'}
                startIcon={<OpenInNewIcon />}
                onClick={() => {
                  window.open(api.ebayConnectUrl, '_blank');
                  setOpened(true);
                }}
              >
                {t('settings.ebay.connectDialog.openEbay')}
              </Button>
            </span>
          </Stack>

          <Stack spacing={1}>
            <Typography variant="subtitle2">{t('settings.ebay.connectDialog.step2Title')}</Typography>
            <Typography variant="body2" color="text.secondary">
              {t('settings.ebay.connectDialog.step2Body')}
            </Typography>
            <TextField
              size="small"
              multiline
              minRows={2}
              maxRows={5}
              label={t('settings.ebay.connectDialog.pasteLabel')}
              placeholder={t('settings.ebay.connectDialog.pastePlaceholder')}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              autoComplete="off"
              spellCheck={false}
            />
            {connect.isError && (
              <Alert severity="error">
                {reason === 'noCode'
                  ? t('settings.ebay.connectDialog.noCode')
                  : reason === 'rejected'
                    ? t('settings.ebay.connectDialog.rejected')
                    : (connect.error as Error).message}
              </Alert>
            )}
          </Stack>

          {showCallbackTip && (
            <Alert severity="info">
              <Typography variant="body2" gutterBottom>
                {t('settings.ebay.connectDialog.callbackTip')}
              </Typography>
              <TextField
                size="small"
                fullWidth
                value={callbackUrl}
                InputProps={{
                  readOnly: true,
                  endAdornment: (
                    <InputAdornment position="end">
                      <Tooltip title={t('settings.ebay.connectDialog.copyCallback')}>
                        <IconButton
                          size="small"
                          edge="end"
                          onClick={() => void navigator.clipboard?.writeText(callbackUrl)}
                        >
                          <ContentCopyIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </InputAdornment>
                  ),
                }}
              />
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => onClose(false)}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={!input.trim() || connect.isPending}
          startIcon={connect.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
          onClick={() => connect.mutate()}
        >
          {connect.isPending ? t('settings.ebay.connectDialog.connecting') : t('settings.ebay.connectDialog.finish')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
