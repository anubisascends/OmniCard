import type { Ref } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Autocomplete, Chip, TextField, createFilterOptions, type SxProps, type Theme } from '@mui/material';
import { api } from '../api/client';
import type { SetInfoDto } from '../api/types';

// Typing finds a set by its code or its name; a code match comes first, so typing a code + Enter picks it.
const matchCodeOrName = createFilterOptions<SetInfoDto>({ stringify: (s) => `${s.setCode} ${s.setName}` });
const filterOptions: typeof matchCodeOrName = (options, state) => {
  const typed = state.inputValue.trim().toLowerCase();
  const rank = (s: SetInfoDto) => {
    const code = s.setCode.toLowerCase();
    return code === typed ? 0 : code.startsWith(typed) ? 1 : 2;
  };
  return matchCodeOrName(options, state)
    .map((s, i) => ({ s, i, r: rank(s) }))
    .sort((a, b) => a.r - b.r || a.i - b.i)
    .map((x) => x.s);
};

/** Pick any number of a game's sets. Options read "Name (CODE)"; chips show just the code, to stay narrow.
 *  Nothing picked means every set. */
export function SetMultiSelect({
  game,
  value,
  onChange,
  label,
  helperText,
  autoFocus,
  inputRef,
  sx,
}: {
  game: string;
  value: SetInfoDto[];
  onChange: (sets: SetInfoDto[]) => void;
  label?: string;
  helperText?: string;
  autoFocus?: boolean;
  inputRef?: Ref<HTMLInputElement>;
  sx?: SxProps<Theme>;
}) {
  const { t } = useTranslation();
  const sets = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: !!game });
  return (
    <Autocomplete
      multiple
      size="small"
      openOnFocus
      autoHighlight
      disableCloseOnSelect
      limitTags={4}
      options={sets.data ?? []}
      loading={sets.isLoading}
      filterOptions={filterOptions}
      getOptionLabel={(s) => `${s.setName} (${s.setCode.toUpperCase()})`}
      isOptionEqualToValue={(a, b) => a.setCode === b.setCode}
      value={value}
      onChange={(_, v) => onChange(v)}
      renderTags={(v, getTagProps) =>
        v.map((s, i) => {
          const { key, ...tagProps } = getTagProps({ index: i });
          return <Chip key={key} {...tagProps} size="small" label={s.setCode.toUpperCase()} title={s.setName} />;
        })
      }
      sx={sx}
      renderInput={(p) => (
        <TextField
          {...p}
          label={label ?? t('common.labels.sets')}
          placeholder={value.length ? '' : t('common.labels.allSets')}
          helperText={helperText}
          autoFocus={autoFocus}
          inputRef={inputRef}
        />
      )}
    />
  );
}
