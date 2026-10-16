import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import BookmarksIcon from '@mui/icons-material/Bookmarks';
import BookmarkAddIcon from '@mui/icons-material/BookmarkAdd';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import DeleteIcon from '@mui/icons-material/Delete';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import FilterListIcon from '@mui/icons-material/FilterList';
import RuleIcon from '@mui/icons-material/Rule';
import { api } from '../api/client';
import { usePermissions } from '../context/usePermissions';
import type { ScanValidationRuleDto } from '../api/types';

/** The outcome of the last Validate press. Replaced wholesale on every run. */
export interface ScanValidationResult {
  /** The query the cards were checked against. */
  query: string;
  /** Keys of the scans that were checked. */
  checked: ReadonlySet<string>;
  /** Keys of the scans that failed (no identity, or didn't match). */
  failed: ReadonlySet<string>;
  /** Scans still matching when Validate was pressed (not checked). */
  skipped: number;
}

/**
 * The Scan page's validation bar: a search query the scan list is checked against (Validate flags every
 * card that doesn't match), saved per-game rules to load it from, and a filter button showing how many
 * cards failed.
 */
export function ScanValidationBar({
  game,
  query,
  onQueryChange,
  onValidate,
  validating,
  canValidate,
  errors,
  result,
  failedCount,
  checkedCount,
  failedOnly,
  onFailedOnlyChange,
}: {
  game: string;
  query: string;
  onQueryChange: (query: string) => void;
  onValidate: () => void;
  validating: boolean;
  /** There are scans to check. */
  canValidate: boolean;
  /** Why the last run checked nothing (invalid query, request failure). */
  errors: string[];
  result: ScanValidationResult | null;
  /** Failed scans still in the list. */
  failedCount: number;
  /** Checked scans still in the list. */
  checkedCount: number;
  failedOnly: boolean;
  onFailedOnlyChange: (on: boolean) => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const canSave = can('scan.commit');
  const [rulesAnchor, setRulesAnchor] = useState<HTMLElement | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [toDelete, setToDelete] = useState<ScanValidationRuleDto | null>(null);

  const rulesKey = ['scan-validation-rules', game];
  const rules = useQuery({ queryKey: rulesKey, queryFn: () => api.scanValidationRules(game), enabled: !!game });
  const remove = useMutation({
    mutationFn: (id: number) => api.scanValidationRuleDelete(id),
    onSuccess: () => {
      setToDelete(null);
      void qc.invalidateQueries({ queryKey: rulesKey });
    },
  });

  const stale = result !== null && result.query !== query.trim();

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
        <TextField
          size="small"
          label={t('scan.validation.label')}
          placeholder={t('scan.validation.placeholder')}
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && canValidate && !validating) {
              e.preventDefault();
              onValidate();
            }
          }}
          InputProps={{ startAdornment: <RuleIcon fontSize="small" sx={{ mr: 0.5, color: 'text.secondary' }} /> }}
          sx={{ flex: '1 1 280px', minWidth: 220 }}
        />
        <Tooltip title={t('scan.validation.savedRules')}>
          <IconButton aria-label={t('scan.validation.savedRules')} onClick={(e) => setRulesAnchor(e.currentTarget)}>
            <BookmarksIcon />
          </IconButton>
        </Tooltip>
        <Button
          variant="contained"
          startIcon={<RuleIcon />}
          disabled={!canValidate || validating || query.trim() === ''}
          onClick={onValidate}
        >
          {validating ? t('scan.validation.validating') : t('scan.validation.validate')}
        </Button>

        {result &&
          (failedCount > 0 ? (
            // describeChild: the tooltip describes the button, so its name stays the "N failed" count.
            <Tooltip describeChild title={failedOnly ? t('scan.validation.showAll') : t('scan.validation.showFailed')}>
              <Button
                variant={failedOnly ? 'contained' : 'outlined'}
                color="error"
                aria-pressed={failedOnly}
                startIcon={<ErrorOutlineIcon />}
                endIcon={<FilterListIcon />}
                onClick={() => onFailedOnlyChange(!failedOnly)}
              >
                {t('scan.validation.failedCount', { count: failedCount })}
              </Button>
            </Tooltip>
          ) : (
            <Chip
              color="success"
              variant="outlined"
              icon={<CheckCircleIcon />}
              label={t('scan.validation.allPassed', { count: checkedCount })}
            />
          ))}
        {result && result.skipped > 0 && (
          <Typography variant="caption" color="text.secondary">
            {t('scan.validation.skipped', { count: result.skipped })}
          </Typography>
        )}
        {stale && (
          <Typography variant="caption" color="warning.main">
            {t('scan.validation.stale')}
          </Typography>
        )}
      </Stack>
      {!result && errors.length === 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          {t('scan.validation.hint')}
        </Typography>
      )}
      {errors.length > 0 && (
        <Alert severity="error" sx={{ mt: 1.5 }}>
          {errors.map((e) => (
            <div key={e}>{e}</div>
          ))}
        </Alert>
      )}

      <Menu anchorEl={rulesAnchor} open={!!rulesAnchor} onClose={() => setRulesAnchor(null)}>
        {(rules.data ?? []).map((r) => (
          <MenuItem
            key={r.id}
            onClick={() => {
              onQueryChange(r.query);
              setRulesAnchor(null);
            }}
            sx={{ maxWidth: 480 }}
          >
            <ListItemText
              primary={r.name}
              secondary={r.query}
              secondaryTypographyProps={{ noWrap: true, sx: { fontFamily: 'monospace' } }}
            />
            {canSave && (
              <IconButton
                size="small"
                edge="end"
                aria-label={t('scan.validation.deleteRule')}
                sx={{ ml: 1 }}
                onClick={(e) => {
                  e.stopPropagation();
                  setRulesAnchor(null);
                  setToDelete(r);
                }}
              >
                <DeleteIcon fontSize="small" />
              </IconButton>
            )}
          </MenuItem>
        ))}
        {rules.data?.length === 0 && (
          <MenuItem disabled>
            <ListItemText primary={t('scan.validation.noSavedRules')} />
          </MenuItem>
        )}
        {canSave && <Divider />}
        {canSave && (
          <MenuItem
            disabled={query.trim() === ''}
            onClick={() => {
              setRulesAnchor(null);
              setSaveOpen(true);
            }}
          >
            <ListItemIcon>
              <BookmarkAddIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary={t('scan.validation.saveRule')} />
          </MenuItem>
        )}
      </Menu>

      <SaveRuleDialog
        open={saveOpen}
        game={game}
        query={query.trim()}
        rules={rules.data ?? []}
        onClose={() => setSaveOpen(false)}
        onSaved={() => {
          setSaveOpen(false);
          void qc.invalidateQueries({ queryKey: rulesKey });
        }}
      />

      <Dialog open={!!toDelete} onClose={() => setToDelete(null)}>
        <DialogTitle>{t('scan.validation.deleteTitle')}</DialogTitle>
        <DialogContent>
          <DialogContentText>{t('scan.validation.deleteBody', { name: toDelete?.name ?? '' })}</DialogContentText>
          {remove.error && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {(remove.error as Error).message}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setToDelete(null)}>{t('common.actions.cancel')}</Button>
          <Button
            color="error"
            variant="contained"
            disabled={remove.isPending}
            onClick={() => toDelete && remove.mutate(toDelete.id)}
          >
            {t('common.actions.delete')}
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}

/** Name the current query and save it for this game; picking an existing name replaces that rule. */
function SaveRuleDialog({
  open,
  game,
  query,
  rules,
  onClose,
  onSaved,
}: {
  open: boolean;
  game: string;
  query: string;
  rules: ScanValidationRuleDto[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const save = useMutation({
    mutationFn: () => api.scanValidationRuleSave(game, name.trim(), query),
    onSuccess: onSaved,
  });
  const replacing = rules.some((r) => r.name.toLowerCase() === name.trim().toLowerCase());

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="xs"
      fullWidth
      TransitionProps={{
        onEnter: () => {
          // Start with the name of the rule this query came from, so re-saving an edit replaces it.
          setName(rules.find((r) => r.query === query)?.name ?? '');
          save.reset();
        },
      }}
    >
      <DialogTitle>{t('scan.validation.saveTitle')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Autocomplete
            freeSolo
            options={rules.map((r) => r.name)}
            inputValue={name}
            onInputChange={(_, v) => setName(v)}
            renderInput={(p) => <TextField {...p} autoFocus label={t('common.labels.name')} />}
          />
          <Box>
            <Typography variant="caption" color="text.secondary">
              {t('scan.validation.label')}
            </Typography>
            <Typography variant="body2" sx={{ fontFamily: 'monospace', wordBreak: 'break-word' }}>
              {query}
            </Typography>
          </Box>
          {replacing && <Alert severity="info">{t('scan.validation.replaces', { name: name.trim() })}</Alert>}
          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!name.trim() || save.isPending} onClick={() => save.mutate()}>
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
