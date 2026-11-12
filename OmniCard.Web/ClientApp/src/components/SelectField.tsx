import {
  Children,
  Fragment,
  isValidElement,
  type FocusEventHandler,
  type Key,
  type KeyboardEventHandler,
  type ReactNode,
  type Ref,
} from 'react';
import {
  Autocomplete,
  Box,
  ListSubheader,
  TextField,
  type FilterOptionsState,
  type SxProps,
  type TextFieldProps,
  type Theme,
} from '@mui/material';
import { useCanHover } from '../lib/responsive';

/** What {@link SelectField} hands `onChange`: shaped like the event a `TextField select` sends, so handlers
 *  written as `(e) => setX(e.target.value)` work unchanged. */
export type SelectFieldChangeEvent<V> = { target: { value: V; name: string } };

/** The `slotProps.select` settings a `TextField select` took that still mean something here. */
type SelectSlotProps<V> = {
  multiple?: boolean;
  /** Accepted for drop-in compatibility: an empty value always shows its option's label here. */
  displayEmpty?: boolean;
  /** Multiple only: what the field shows for the ticked options (an empty pick's text becomes the placeholder). */
  renderValue?: (value: V) => ReactNode;
};

type TextFieldSlotProps = NonNullable<TextFieldProps['slotProps']>;

export type SelectFieldProps<V> = {
  value: V;
  onChange?: (event: SelectFieldChangeEvent<V>) => void;
  /** `<MenuItem value=…>` options, optionally headed by `<ListSubheader>` groups (fragments and arrays are fine). */
  children?: ReactNode;
  label?: ReactNode;
  size?: 'small' | 'medium';
  sx?: SxProps<Theme>;
  fullWidth?: boolean;
  disabled?: boolean;
  required?: boolean;
  error?: boolean;
  helperText?: ReactNode;
  placeholder?: string;
  name?: string;
  id?: string;
  margin?: TextFieldProps['margin'];
  autoFocus?: boolean;
  inputRef?: Ref<HTMLInputElement>;
  /** Open the list as soon as the field gets focus (e.g. when a shortcut key jumps to it). */
  openOnFocus?: boolean;
  onKeyDown?: KeyboardEventHandler<HTMLDivElement>;
  onBlur?: FocusEventHandler<HTMLDivElement>;
  slotProps?: Omit<TextFieldSlotProps, 'select' | 'input' | 'inputLabel' | 'htmlInput'> & {
    select?: SelectSlotProps<V>;
    input?: object;
    inputLabel?: object;
    htmlInput?: object;
  };
};

type Option = {
  value: unknown;
  /** Plain text of the option: what's typed against and what the field shows once picked. */
  label: string;
  /** The option as the caller drew it (icons, emphasis, checkboxes…), shown in the list. */
  node: ReactNode;
  disabled: boolean;
  group?: string;
  index: number;
};

type ElementProps = { children?: ReactNode; value?: unknown; disabled?: boolean; primary?: ReactNode };

/** The text a node renders (`<ListItemText primary>` counts), whitespace collapsed. */
function textOf(node: ReactNode): string {
  const walk = (n: ReactNode): string => {
    if (n == null || typeof n === 'boolean') return '';
    if (typeof n === 'string' || typeof n === 'number') return String(n);
    if (Array.isArray(n)) return n.map(walk).join('');
    if (isValidElement<ElementProps>(n)) return walk(n.props.primary) + walk(n.props.children);
    return '';
  };
  return walk(node).replace(/\s+/g, ' ').trim();
}

/** The options a `TextField select`'s children describe: each `MenuItem` (anything with a `value`), grouped
 *  under the `ListSubheader` before it. Dividers and other decoration are dropped. */
function collectOptions(children: ReactNode): { options: Option[]; grouped: boolean } {
  const options: Option[] = [];
  let group: string | undefined;
  let grouped = false;
  const walk = (nodes: ReactNode) =>
    Children.forEach(nodes, (child) => {
      if (!isValidElement<ElementProps>(child)) return;
      if (child.type === Fragment) return walk(child.props.children);
      if (child.type === ListSubheader) {
        group = textOf(child.props.children);
        grouped = true;
        return;
      }
      if (!('value' in child.props)) return;
      options.push({
        value: child.props.value,
        label: textOf(child.props.children),
        node: child.props.children,
        disabled: !!child.props.disabled,
        group,
        index: options.length,
      });
    });
  walk(children);
  return { options, grouped };
}

/** Same equality a MUI Select uses: 5 matches "5". */
const sameValue = (a: unknown, b: unknown) =>
  a === b || (a != null && b != null && typeof b !== 'object' && String(a) === String(b));

/** Case- and accent-insensitive ("poke" finds Pokémon). */
const fold = (s: string) =>
  s
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();

const isWordChar = (c: string) => /[\p{L}\p{N}]/u.test(c);

/** How well `label` matches `query`: 0 = starts with it, 1 = a word starts with it, 2 = contains it, -1 = no match. */
function rank(label: string, query: string): number {
  const l = fold(label);
  if (l.startsWith(query)) return 0;
  let best = -1;
  for (let i = l.indexOf(query); i >= 0; i = l.indexOf(query, i + 1)) {
    if (!isWordChar(l[i - 1])) return 1;
    best = 2;
  }
  return best;
}

/** Options matching what's typed, best first (start of the option, then start of a word, then anywhere) so
 *  Enter takes the likeliest one. Grouped lists keep their order so each group stays together. */
const filterFor =
  (grouped: boolean) =>
  (options: Option[], { inputValue }: FilterOptionsState<Option>): Option[] => {
    const query = fold(inputValue);
    if (!query) return options;
    const ranked = options.map((o) => ({ o, r: rank(o.label, query) })).filter((x) => x.r >= 0);
    if (!grouped) ranked.sort((a, b) => a.r - b.r || a.o.index - b.o.index);
    return ranked.map((x) => x.o);
  };

/**
 * A drop-in for `<TextField select>` that you can type into: the list narrows to the options matching
 * what's typed (from the start of a word first), the best one is highlighted, and Enter picks it. Takes
 * the same props and `<MenuItem>` / `<ListSubheader>` children, and calls `onChange` with an event-shaped
 * `{ target: { value } }` carrying the picked item's own `value`. `slotProps.select.multiple` makes it a
 * multi-pick that stays open while ticking. On touch screens (no hover) typing is off: a tap opens the list.
 */
export function SelectField<V>({
  value,
  onChange,
  children,
  label,
  size,
  sx,
  fullWidth,
  disabled,
  required,
  error,
  helperText,
  placeholder,
  name,
  id,
  margin,
  autoFocus,
  inputRef,
  openOnFocus,
  onKeyDown,
  onBlur,
  slotProps,
}: SelectFieldProps<V>) {
  const { select: selectSlot, input: inputSlot, inputLabel: labelSlot, htmlInput: htmlSlot, ...otherSlots } = slotProps ?? {};
  const multiple = !!selectSlot?.multiple;
  // Typing to filter needs a keyboard: on touch screens the box is read-only, so a tap just opens the
  // list instead of raising the on-screen keyboard over it.
  const typing = useCanHover();
  const { options, grouped } = collectOptions(children);

  const values = multiple ? ((value as unknown[] | null) ?? []) : [];
  const selected = multiple
    ? options.filter((o) => values.some((v) => sameValue(v, o.value)))
    : (options.find((o) => sameValue(value, o.value)) ?? null);

  // A multi-pick's own summary (e.g. "English, Japanese"), and its empty text as the placeholder.
  const summary = multiple && selectSlot?.renderValue && values.length ? selectSlot.renderValue(value) : null;
  const emptyText = multiple && !values.length ? selectSlot?.renderValue?.(value) : null;
  const shownPlaceholder = placeholder ?? (typeof emptyText === 'string' ? emptyText : undefined);

  const emit = (picked: unknown) => onChange?.({ target: { value: picked as V, name: name ?? '' } });

  return (
    <Autocomplete<Option, boolean, boolean, false>
      multiple={multiple}
      disableClearable
      disableCloseOnSelect={multiple}
      autoHighlight
      selectOnFocus={typing}
      openOnFocus={openOnFocus}
      options={options}
      value={selected as Option | Option[]}
      onChange={(_, picked) => {
        if (picked == null) return;
        emit(Array.isArray(picked) ? picked.map((o) => o.value) : picked.value);
      }}
      getOptionLabel={(o) => o.label}
      getOptionKey={(o) => o.index}
      getOptionDisabled={(o) => o.disabled}
      isOptionEqualToValue={(o, v) => sameValue(v.value, o.value)}
      filterOptions={filterFor(grouped)}
      groupBy={grouped ? (o) => o.group ?? '' : undefined}
      renderGroup={(params) =>
        params.group ? (
          <li key={params.key}>
            <ListSubheader component="div" sx={{ top: -8 }}>
              {params.group}
            </ListSubheader>
            <Box component="ul" sx={{ p: 0 }}>
              {params.children}
            </Box>
          </li>
        ) : (
          <Fragment key={params.key}>{params.children}</Fragment>
        )
      }
      renderOption={(props, option) => {
        const { key, ...rest } = props as typeof props & { key: Key };
        return (
          <li key={key} {...rest}>
            {option.node}
          </li>
        );
      }}
      renderTags={
        summary != null
          ? () => (
              <Box
                component="span"
                sx={{ pl: 0.75, minWidth: 0, ...(typeof summary === 'string' && { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }) }}
              >
                {summary}
              </Box>
            )
          : undefined
      }
      size={size}
      disabled={disabled}
      fullWidth={fullWidth}
      onKeyDown={onKeyDown}
      onBlur={onBlur}
      slotProps={{ popper: { sx: { minWidth: 'max-content', maxWidth: 'calc(100vw - 32px)' } } }}
      // A select's width follows its sx; without one it sits inline at the input's own width, as a select did.
      sx={[
        !fullWidth && { display: 'inline-flex', verticalAlign: 'top' },
        !typing && { '& .MuiAutocomplete-input': { cursor: 'pointer', caretColor: 'transparent' } },
        typeof summary === 'string' && { '& .MuiAutocomplete-inputRoot': { flexWrap: 'nowrap' } },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      renderInput={({ InputProps, InputLabelProps, inputProps, ...params }) => (
        <TextField
          {...params}
          fullWidth
          id={id ?? params.id}
          label={label}
          placeholder={shownPlaceholder}
          required={required}
          error={error}
          helperText={helperText}
          name={name}
          margin={margin}
          autoFocus={autoFocus}
          inputRef={inputRef}
          slotProps={{
            ...otherSlots,
            input: { ...InputProps, ...inputSlot },
            inputLabel: { ...InputLabelProps, ...labelSlot },
            htmlInput: { ...inputProps, readOnly: !typing, ...htmlSlot },
          }}
        />
      )}
    />
  );
}
