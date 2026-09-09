import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  IconButton,
  InputAdornment,
  Popover,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import HelpOutlineIcon from '@mui/icons-material/HelpOutline';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

interface SearchBoxProps {
  /** Current input text. */
  value: string;
  onChange: (value: string) => void;
  /** Fired on Enter (form submit). Omit for callers that filter as-you-type via `onChange`. */
  onSubmit?: (value: string) => void;
  size?: 'small' | 'medium';
  fullWidth?: boolean;
  /** Override the game whose fields drive the hints (defaults to the app-wide selected game). */
  game?: string;
  autoFocus?: boolean;
}

/**
 * Collection/catalog search input with a game-aware dynamic placeholder and a syntax-help popover.
 * The field vocabulary comes from `/api/meta/search-fields?game=` so each game surfaces its own
 * tokens (e.g. Final Fantasy `element:fire`) instead of a hardcoded MTG-flavored hint.
 */
export function SearchBox({
  value,
  onChange,
  onSubmit,
  size = 'small',
  fullWidth = true,
  game,
  autoFocus,
}: SearchBoxProps) {
  const { game: activeGame } = useGame();
  const effectiveGame = game ?? activeGame;
  const [helpAnchor, setHelpAnchor] = useState<HTMLElement | null>(null);

  const { data: schema } = useQuery({
    queryKey: ['searchFields', effectiveGame ?? 'all'],
    queryFn: () => api.searchFields(effectiveGame),
    staleTime: 5 * 60 * 1000,
  });

  const placeholder = useMemo(() => {
    if (!schema) return 'Search…';
    const examples = schema.fields
      .filter((f) => f.example)
      .slice(0, 5)
      .map((f) => f.example);
    return examples.length ? `Search — try ${examples.join(', ')}` : 'Search…';
  }, [schema]);

  return (
    <Box
      component="form"
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit?.(value);
      }}
    >
      <TextField
        fullWidth={fullWidth}
        size={size}
        placeholder={placeholder}
        value={value}
        autoFocus={autoFocus}
        onChange={(e) => onChange(e.target.value)}
        InputProps={{
          endAdornment: (
            <InputAdornment position="end">
              <Tooltip title="Search syntax help">
                <IconButton
                  edge="end"
                  size="small"
                  aria-label="Search syntax help"
                  onClick={(e) => setHelpAnchor(e.currentTarget)}
                >
                  <HelpOutlineIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </InputAdornment>
          ),
        }}
      />

      <Popover
        open={Boolean(helpAnchor)}
        anchorEl={helpAnchor}
        onClose={() => setHelpAnchor(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
      >
        <Box sx={{ p: 2, maxWidth: 420 }}>
          <Typography variant="subtitle2" gutterBottom>
            Search syntax
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            Combine tokens with spaces (AND), <code>or</code>, <code>-</code> to negate, and parentheses.
            Plain words match the card name.
          </Typography>
          <Stack spacing={1}>
            {schema?.fields.map((f) => (
              <Box key={f.canonical}>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  {f.aliases.join(' / ')}
                  {f.example ? (
                    <Typography component="span" variant="body2" color="primary.main" sx={{ ml: 1 }}>
                      <code>{f.example}</code>
                    </Typography>
                  ) : null}
                </Typography>
                {f.description ? (
                  <Typography variant="caption" color="text.secondary">
                    {f.description}
                  </Typography>
                ) : null}
                {f.valueAliases.length ? (
                  <Typography variant="caption" color="text.secondary" display="block">
                    Values: {f.valueAliases.join(', ')}
                  </Typography>
                ) : null}
              </Box>
            ))}
          </Stack>
        </Box>
      </Popover>
    </Box>
  );
}
