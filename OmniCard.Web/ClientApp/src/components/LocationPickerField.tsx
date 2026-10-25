import { useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { IconButton, InputAdornment, TextField, type SxProps, type Theme } from '@mui/material';
import PlaceIcon from '@mui/icons-material/Place';
import ClearIcon from '@mui/icons-material/Clear';
import { api } from '../api/client';
import { LocationPickerDialog } from './dialogs/LocationPickerDialog';

/**
 * Form-field front end for the shared `LocationPickerDialog`: a read-only, labeled field showing the
 * chosen location's name that opens the grouped/searchable picker (with inline create) on click.
 * Use this anywhere a form needs a location value instead of a `<TextField select>` combobox.
 */
export function LocationPickerField({
  label,
  value,
  onChange,
  clearable = false,
  emptyText,
  dialogTitle,
  required,
  disabled,
  size,
  allowCreate,
  includeReadOnly,
  types,
  cardGames,
  sx,
}: {
  label: string;
  value: number | null;
  onChange: (id: number | null) => void;
  /** Shows a clear button so the value can be set back to "no location". */
  clearable?: boolean;
  /** Placeholder shown while no location is chosen (e.g. "— none —"). */
  emptyText?: string;
  /** Picker dialog title; defaults to the field label. */
  dialogTitle?: string;
  required?: boolean;
  disabled?: boolean;
  size?: 'small' | 'medium';
  allowCreate?: boolean;
  includeReadOnly?: boolean;
  types?: string[];
  cardGames?: string[];
  sx?: SxProps<Theme>;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const name = value == null ? '' : (locations.data?.find((l) => l.id === value)?.name ?? `#${value}`);
  const openPicker = () => {
    if (!disabled) setOpen(true);
  };

  return (
    <>
      <TextField
        label={label}
        size={size}
        value={name}
        placeholder={emptyText}
        required={required}
        disabled={disabled}
        onClick={openPicker}
        sx={[{ '& .MuiInputBase-root, & input': { cursor: disabled ? 'default' : 'pointer' } }, ...(Array.isArray(sx) ? sx : [sx])]}
        slotProps={{
          input: {
            readOnly: true,
            startAdornment: (
              <InputAdornment position="start">
                <PlaceIcon fontSize="small" />
              </InputAdornment>
            ),
            endAdornment:
              clearable && value != null && !disabled ? (
                <InputAdornment position="end">
                  <IconButton
                    size="small"
                    edge="end"
                    aria-label={t('common.actions.clear')}
                    title={t('common.actions.clear')}
                    onClick={(e) => {
                      e.stopPropagation();
                      onChange(null);
                    }}
                  >
                    <ClearIcon fontSize="small" />
                  </IconButton>
                </InputAdornment>
              ) : undefined,
          },
          htmlInput: {
            onKeyDown: (e: KeyboardEvent) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                openPicker();
              }
            },
          },
        }}
      />
      <LocationPickerDialog
        open={open}
        title={dialogTitle ?? label}
        allowCreate={allowCreate}
        includeReadOnly={includeReadOnly}
        types={types}
        cardGames={cardGames}
        onPick={(id) => {
          onChange(id);
          setOpen(false);
        }}
        onClose={() => setOpen(false)}
      />
    </>
  );
}
