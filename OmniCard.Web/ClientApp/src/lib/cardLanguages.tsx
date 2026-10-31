import { useCallback, useState } from 'react';
import { Checkbox, Chip, ListItemText, MenuItem, TextField, Tooltip, type SxProps, type Theme } from '@mui/material';
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
 * The languages `game`'s catalog can hold rows in (English first) — what a catalog search can filter by.
 * English-only catalogs return just English, as does the hook while loading.
 */
export function useCatalogLanguages(game: string): string[] {
  const query = useQuery({
    queryKey: ['catalog-search-languages'],
    queryFn: api.catalogSearchLanguages,
    staleTime: Infinity,
  });
  return query.data?.[game] ?? [ENGLISH];
}

const filterKey = (scope: string, game: string) => `omnicard.languageFilter.${scope}.${game}`;

function readFilter(scope: string, game: string): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(filterKey(scope, game)) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : [];
  } catch {
    return [];
  }
}

/**
 * A catalog-search language filter remembered per `scope` + game in this browser, so the last choice
 * comes back the next time the picker opens. Empty = every language. Only codes in `available` are
 * returned, so a remembered language the catalog no longer holds doesn't silently hide everything.
 */
export function useRememberedLanguageFilter(
  scope: string,
  game: string,
  available: string[],
): [string[], (languages: string[]) => void] {
  // Re-read whenever the game changes (each game remembers its own choice).
  const [stored, setStored] = useState(() => ({ game, languages: readFilter(scope, game) }));
  const current = stored.game === game ? stored.languages : readFilter(scope, game);
  if (stored.game !== game) setStored({ game, languages: current });

  const set = useCallback(
    (languages: string[]) => {
      setStored({ game, languages });
      try {
        localStorage.setItem(filterKey(scope, game), JSON.stringify(languages));
      } catch {
        // Storage blocked (private window etc.): the choice still applies for this session.
      }
    },
    [scope, game],
  );
  return [current.filter((l) => available.includes(l)), set];
}

/**
 * Multi-select of catalog languages to search. Nothing ticked = every language (shown as "All
 * languages"), so there's always a way back to the unfiltered list.
 */
export function LanguageFilterSelect({
  languages,
  value,
  onChange,
  sx,
}: {
  languages: string[];
  value: string[];
  onChange: (languages: string[]) => void;
  sx?: SxProps<Theme>;
}) {
  const { t } = useTranslation();
  return (
    <TextField
      select
      size="small"
      label={t('common.labels.languages')}
      value={value}
      onChange={(e) => {
        const v = e.target.value as unknown as string[] | string;
        const next = typeof v === 'string' ? v.split(',') : v;
        // Ticking every language is the same as no filter — store it that way.
        onChange(next.length >= languages.length ? [] : languages.filter((l) => next.includes(l)));
      }}
      slotProps={{
        select: {
          multiple: true,
          displayEmpty: true,
          renderValue: (selected) => {
            const codes = selected as string[];
            return codes.length === 0
              ? t('common.labels.allLanguages')
              : codes.map((c) => languageName(t, c)).join(', ');
          },
        },
        inputLabel: { shrink: true },
      }}
      sx={sx ?? { width: 200 }}
    >
      {languages.map((code) => (
        <MenuItem key={code} value={code} dense>
          <Checkbox size="small" checked={value.includes(code)} sx={{ py: 0 }} />
          <ListItemText primary={languageName(t, code)} />
        </MenuItem>
      ))}
    </TextField>
  );
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
  disabled,
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
  disabled?: boolean;
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
      disabled={disabled}
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

/** Small "JA"-style chip for a non-English copy; renders nothing for English unless `showEnglish` (for
 * lists that mix languages, where an unlabelled row would be ambiguous). */
export function LanguageChip({
  language,
  detected,
  showEnglish = false,
}: {
  language?: string | null;
  detected?: boolean;
  showEnglish?: boolean;
}) {
  const { t } = useTranslation();
  if (!language && showEnglish) language = ENGLISH;
  if (!language || (language === ENGLISH && !showEnglish)) return null;
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
