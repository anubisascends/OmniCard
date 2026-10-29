import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  InputAdornment,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import type { CustomerDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { usePermissions } from '../../context/usePermissions';
import { useIsPhone } from '../../lib/responsive';

/** A customer's store credit: the balance, the ledger (intake credits link to their intake), and — with
 * the Adjust store credit permission — a manual credit or debit with a reason. */
export function StoreCreditDialog({ customer, onClose }: { customer: CustomerDto | null; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const isPhone = useIsPhone();
  const id = customer?.id ?? null;
  const credit = useQuery({ queryKey: ['customer-credit', id], queryFn: () => api.customerCredit(id!), enabled: id != null });
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');

  const adjust = useMutation({
    mutationFn: () => api.customerCreditAdjust(id!, Number(amount), reason.trim()),
    onSuccess: (updated) => {
      qc.setQueryData(['customer-credit', id], updated);
      void qc.invalidateQueries({ queryKey: ['customers'] });
      setAmount('');
      setReason('');
    },
  });
  const amountValue = Number(amount);
  const canApply = amount.trim() !== '' && !Number.isNaN(amountValue) && amountValue !== 0 && reason.trim() !== '';

  return (
    <Dialog open={customer !== null} onClose={onClose} maxWidth="md" fullWidth fullScreen={isPhone}>
      <DialogTitle>{t('sales.customers.credit.title', { name: customer?.name ?? '' })}</DialogTitle>
      <DialogContent dividers>
        {credit.isLoading && <CircularProgress />}
        {credit.isError && <Alert severity="error">{(credit.error as Error).message}</Alert>}
        {credit.data && (
          <Stack spacing={2}>
            <Box>
              <Typography variant="overline" color="text.secondary">
                {t('sales.customers.credit.balance')}
              </Typography>
              <Typography variant="h4">{fmt.money(credit.data.balance)}</Typography>
            </Box>

            {can('sales.customers.credit') && (
              <Paper variant="outlined" sx={{ p: 2 }}>
                <Typography variant="subtitle2">{t('sales.customers.credit.adjustTitle')}</Typography>
                <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
                  {t('sales.customers.credit.adjustHelp')}
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ sm: 'flex-start' }}>
                  <TextField
                    size="small"
                    type="number"
                    label={t('sales.customers.credit.amount')}
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    inputProps={{ step: 0.01, inputMode: 'decimal' }}
                    InputProps={{ startAdornment: <InputAdornment position="start">$</InputAdornment> }}
                    sx={{ width: { sm: 160 } }}
                  />
                  <TextField
                    size="small"
                    label={t('sales.customers.credit.reason')}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    inputProps={{ maxLength: 300 }}
                    sx={{ flexGrow: 1 }}
                  />
                  <Button variant="contained" disabled={!canApply || adjust.isPending} onClick={() => adjust.mutate()}>
                    {t('sales.customers.credit.apply')}
                  </Button>
                </Stack>
                {adjust.isError && (
                  <Alert severity="error" sx={{ mt: 1 }}>
                    {(adjust.error as Error).message}
                  </Alert>
                )}
              </Paper>
            )}

            <Typography variant="subtitle2">{t('sales.customers.credit.history')}</Typography>
            {credit.data.entries.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                {t('sales.customers.credit.empty')}
              </Typography>
            ) : (
              <TableContainer component={Paper} variant="outlined">
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>{t('sales.customers.credit.colDate')}</TableCell>
                      <TableCell align="right">{t('sales.customers.credit.colAmount')}</TableCell>
                      <TableCell>{t('sales.customers.credit.colReason')}</TableCell>
                      <TableCell>{t('sales.customers.credit.colBy')}</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {credit.data.entries.map((e) => (
                      <TableRow key={e.id}>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{fmt.dateTime(e.createdAt)}</TableCell>
                        <TableCell align="right" sx={{ color: e.amount < 0 ? 'error.main' : 'success.main' }}>
                          {fmt.money(e.amount)}
                        </TableCell>
                        <TableCell>
                          {e.intakeId != null && can('intake.history') ? (
                            <RouterLink to={`/intake/history?id=${e.intakeId}`}>
                              {t('sales.customers.credit.intake', { id: e.intakeId })}
                            </RouterLink>
                          ) : (
                            e.reason
                          )}
                        </TableCell>
                        <TableCell>{e.createdByName}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>
    </Dialog>
  );
}
