import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Chip, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import type { DeckReturnLineDto } from '../api/types';
import { useFormatters } from '../i18n/format';
import { LanguageChip } from '../lib/cardLanguages';
import { returnIssue, returnPosition } from '../lib/deckReturn';

/**
 * A put-back list: returned cards grouped by the location they go to (with the site when there are
 * several), in the server's walk order (section ▸ page ▸ slot). Cards that couldn't go back where they came
 * from are highlighted with the reason.
 */
export function DeckReturnLinesTable({ lines }: { lines: DeckReturnLineDto[] }) {
  const { t } = useTranslation();
  const fmt = useFormatters();

  const groups = useMemo(() => {
    const byDest = new Map<string, { title: string; lines: DeckReturnLineDto[] }>();
    const multipleSites = new Set(lines.map((l) => l.siteName)).size > 1;
    for (const l of lines) {
      const key = `${l.siteName ?? ''}|${l.destinationId}`;
      const title = multipleSites && l.siteName ? `${l.siteName} ▸ ${l.destinationName}` : l.destinationName;
      const group = byDest.get(key) ?? { title, lines: [] };
      group.lines.push(l);
      byDest.set(key, group);
    }
    return [...byDest.values()];
  }, [lines]);

  if (lines.length === 0)
    return (
      <Typography variant="body2" color="text.secondary">
        {t('deckbox.return.nothing')}
      </Typography>
    );

  return (
    <Stack spacing={2}>
      {groups.map((g) => (
        <Box key={g.title}>
          <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
            {t('deckbox.return.groupTitle', {
              name: g.title,
              copies: fmt.number(g.lines.reduce((n, l) => n + l.quantity, 0)),
            })}
          </Typography>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell align="right" sx={{ width: 56 }}>
                  {t('common.labels.quantity')}
                </TableCell>
                <TableCell>{t('common.labels.name')}</TableCell>
                <TableCell>{t('deckbox.sync.columns.printing')}</TableCell>
                <TableCell>{t('common.labels.condition')}</TableCell>
                <TableCell>{t('deckbox.return.where')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {g.lines.map((l, i) => {
                const issue = returnIssue(t, l);
                return (
                  <TableRow key={`${l.lotId}-${i}`} sx={issue ? { bgcolor: 'warning.light', '& td': { color: 'warning.contrastText' } } : undefined}>
                    <TableCell align="right">{fmt.number(l.quantity)}</TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={0.5} alignItems="center" flexWrap="wrap" useFlexGap>
                        <span>
                          {l.cardName}
                          {l.isFoil ? ' ✦' : ''}
                        </span>
                        <LanguageChip language={l.language} />
                        {l.rejoinedStack && (
                          <Chip size="small" variant="outlined" label={t('deckbox.return.rejoins')} sx={{ height: 20, fontSize: 11 }} />
                        )}
                      </Stack>
                      {issue && (
                        <Stack direction="row" spacing={0.5} alignItems="center">
                          <WarningAmberIcon sx={{ fontSize: 14 }} />
                          <Typography variant="caption">{issue}</Typography>
                        </Stack>
                      )}
                    </TableCell>
                    <TableCell>
                      {l.setCode?.toUpperCase() ?? ''}
                      {l.collectorNumber ? ` #${l.collectorNumber}` : ''}
                    </TableCell>
                    <TableCell>{l.condition ? t(`common.conditions.${l.condition}`, { defaultValue: l.condition }) : '—'}</TableCell>
                    <TableCell>{returnPosition(t, l)}</TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </Box>
      ))}
    </Stack>
  );
}
