import { Chip, MenuItem, TextField, Tooltip, type SxProps, type Theme } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';

/** English — the implied language of every copy that isn't tagged otherwise. */
export const ENGLISH = 'en';

/**
 * The languages an owned copy of `game` can be tagged with (English first), from the server's
 * per-game list. With no game (a mixed-game selection) it's the union across games. Falls back to
 * just English while loading.
 */
export function useCardLanguages(game: string | null | undefined): string[] {
  const query = useQuery({
    queryKey: ['card-languages'],
    queryFn: api.cardLanguages,
    staleTime: Infinity,
  });
  const byGame = query.data;
  if (!byGame) return [ENGLISH];
  if (game) return byGame[game] ?? [ENGLISH];
  return [...new Set(Object.values(byGame).flat())];
}

/**
 * Language dropdown for a card copy. `allowAuto` adds a leading "Auto (detect)" option whose value is
 * the empty string (the scan session's default). A current value outside the game's list is still
 * shown so an imported/legacy tag isn't silently dropped.
 */
export function LanguageSelect({
  game,
  value,
  onChange,
  allowAuto = false,
  emptyLabel,
  label,
  fullWidth,
  sx,
  helperText,
}: {
  game: string | null | undefined;
  value: string;
  onChange: (language: string) => void;
  allowAuto?: boolean;
  /** Label for the leading empty-value option shown with `allowAuto` (defaults to "Auto (detect)"). */
  emptyLabel?: string;
  label?: string;
  fullWidth?: boolean;
  sx?: SxProps<Theme>;
  helperText?: string;
}) {
  const { t } = useTranslation();
  const languages = useCardLanguages(game);
  const options = value && !languages.includes(value) ? [...languages, value] : languages;
  return (
    <TextField
      select
      size="small"
      label={label ?? t('common.labels.language')}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      fullWidth={fullWidth}
      helperText={helperText}
      sx={sx ?? { minWidth: 150 }}
    >
      {allowAuto && <MenuItem value="">{emptyLabel ?? t('scan.controls.languageAuto')}</MenuItem>}
      {options.map((code) => (
        <MenuItem key={code} value={code}>
          {languageName(t, code)}
        </MenuItem>
      ))}
    </TextField>
  );
}

/** Localized display name for a language code (falls back to the upper-cased code). */
export function languageName(t: TFunction, code: string): string {
  return t(`common.languages.${code}`, { defaultValue: code.toUpperCase() });
}

/** Small "JA"-style chip for a non-English copy; renders nothing for English. */
export function LanguageChip({ language, detected }: { language?: string | null; detected?: boolean }) {
  const { t } = useTranslation();
  if (!language || language === ENGLISH) return null;
  const chip = (
    <Chip
      size="small"
      variant={detected ? 'filled' : 'outlined'}
      color="info"
      label={language.toUpperCase()}
      sx={{ height: 20, fontSize: 11, fontWeight: 600 }}
    />
  );
  return (
    <Tooltip title={detected ? `${languageName(t, language)} — ${t('scan.props.languageDetected')}` : languageName(t, language)}>
      {chip}
    </Tooltip>
  );
}
