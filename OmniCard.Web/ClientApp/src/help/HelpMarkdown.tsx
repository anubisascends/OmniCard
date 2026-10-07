import { Fragment, useState, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert,
  AlertTitle,
  Box,
  Dialog,
  DialogContent,
  Divider,
  IconButton,
  Link,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ImageOutlinedIcon from '@mui/icons-material/ImageOutlined';
import LightbulbOutlinedIcon from '@mui/icons-material/LightbulbOutlined';
import { useTranslation } from 'react-i18next';
import { helpImageUrl, slugify } from './content';

// A deliberately small Markdown renderer for the help topics — just the subset the topics are written
// in (headings, paragraphs, one-level-nested lists, bold/italic/code, links, images, GitHub-style
// callouts, pipe tables, rules) — mapped onto MUI so help follows the app theme (incl. dark mode).
// Link targets: `help:<topic>[#anchor]` → another topic, `/route` → an app page, `#anchor` → this
// topic, anything else opens in a new tab.

type Align = 'left' | 'center' | 'right' | undefined;
type CalloutKind = 'tip' | 'note' | 'warning' | 'quote';

export type Block =
  | { kind: 'heading'; level: number; text: string; id: string }
  | { kind: 'paragraph'; text: string }
  | { kind: 'list'; ordered: boolean; start: number; items: Block[][] }
  | { kind: 'callout'; variant: CalloutKind; blocks: Block[] }
  | { kind: 'table'; header: string[]; align: Align[]; rows: string[][] }
  | { kind: 'image'; alt: string; src: string }
  | { kind: 'hr' };

const HEADING = /^(#{1,6})\s+(.*)$/;
const HR = /^\s*(-{3,}|\*{3,}|_{3,})\s*$/;
const IMAGE = /^!\[([^\]]*)\]\(([^)\s]+)\)\s*$/;
const LIST_ITEM = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const indentOf = (line: string) => line.match(/^\s*/)![0].length;

function isBlockStart(line: string): boolean {
  return HEADING.test(line) || HR.test(line) || IMAGE.test(line) || /^\s*>/.test(line) || /^\s*\|/.test(line) || LIST_ITEM.test(line);
}

function splitRow(line: string): string[] {
  return line
    .trim()
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split(/(?<!\\)\|/)
    .map((c) => c.trim().replace(/\\\|/g, '|'));
}

export function parseBlocks(markdown: string): Block[] {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks: Block[] = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) {
      i++;
      continue;
    }

    const heading = line.match(HEADING);
    if (heading) {
      const text = heading[2].trim();
      blocks.push({ kind: 'heading', level: heading[1].length, text, id: slugify(text) });
      i++;
      continue;
    }

    if (HR.test(line)) {
      blocks.push({ kind: 'hr' });
      i++;
      continue;
    }

    const image = line.match(IMAGE);
    if (image) {
      blocks.push({ kind: 'image', alt: image[1], src: image[2] });
      i++;
      continue;
    }

    if (/^\s*>/.test(line)) {
      const inner: string[] = [];
      while (i < lines.length && /^\s*>/.test(lines[i])) inner.push(lines[i++].replace(/^\s*>\s?/, ''));
      const marker = inner[0]?.match(/^\[!(TIP|NOTE|WARNING|IMPORTANT|CAUTION)\]\s*$/i);
      const variant: CalloutKind = !marker
        ? 'quote'
        : marker[1].toUpperCase() === 'TIP'
          ? 'tip'
          : ['WARNING', 'CAUTION'].includes(marker[1].toUpperCase())
            ? 'warning'
            : 'note';
      blocks.push({ kind: 'callout', variant, blocks: parseBlocks((marker ? inner.slice(1) : inner).join('\n')) });
      continue;
    }

    if (/^\s*\|/.test(line)) {
      const rows: string[] = [];
      while (i < lines.length && /^\s*\|/.test(lines[i])) rows.push(lines[i++]);
      const header = splitRow(rows[0]);
      const hasSeparator = rows.length > 1 && /^[\s|:-]+$/.test(rows[1]);
      const align: Align[] = hasSeparator
        ? splitRow(rows[1]).map((c) => (c.startsWith(':') && c.endsWith(':') ? 'center' : c.endsWith(':') ? 'right' : undefined))
        : [];
      blocks.push({ kind: 'table', header, align, rows: rows.slice(hasSeparator ? 2 : 1).map(splitRow) });
      continue;
    }

    const first = line.match(LIST_ITEM);
    if (first) {
      const baseIndent = first[1].length;
      const ordered = /\d/.test(first[2]);
      const items: string[][] = [];
      while (i < lines.length) {
        const l = lines[i];
        const m = l.match(LIST_ITEM);
        if (m && m[1].length === baseIndent && /\d/.test(m[2]) === ordered) {
          items.push([m[3]]);
          i++;
          continue;
        }
        if (!l.trim()) {
          // A blank line ends the list unless the next content line continues it (same kind) or is
          // indented under the current item.
          let j = i + 1;
          while (j < lines.length && !lines[j].trim()) j++;
          const next = lines[j];
          const nm = next?.match(LIST_ITEM);
          const continues =
            next !== undefined &&
            ((nm && nm[1].length === baseIndent && /\d/.test(nm[2]) === ordered) || indentOf(next) > baseIndent);
          if (!continues) break;
          items[items.length - 1].push('');
          i++;
          continue;
        }
        if (indentOf(l) > baseIndent) {
          items[items.length - 1].push(l);
          i++;
          continue;
        }
        // Lazy continuation of the item's paragraph.
        if (!isBlockStart(l)) {
          items[items.length - 1].push(l);
          i++;
          continue;
        }
        break;
      }
      blocks.push({
        kind: 'list',
        ordered,
        start: ordered ? parseInt(first[2], 10) || 1 : 1,
        items: items.map((itemLines) => {
          const [head, ...rest] = itemLines;
          const minIndent = Math.min(...rest.filter((r) => r.trim()).map(indentOf), Infinity);
          const dedented = rest.map((r) => (r.trim() ? r.slice(Number.isFinite(minIndent) ? minIndent : 0) : ''));
          return parseBlocks([head, ...dedented].join('\n'));
        }),
      });
      continue;
    }

    const para: string[] = [];
    while (i < lines.length && lines[i].trim() && (para.length === 0 || !isBlockStart(lines[i]))) para.push(lines[i++].trim());
    blocks.push({ kind: 'paragraph', text: para.join(' ') });
  }
  return blocks;
}

// ---- inline -------------------------------------------------------------------------------------

const INLINE = /`([^`]+)`|\*\*(.+?)\*\*|\[([^\]]+)\]\(([^)\s]+)\)|(?<![\w*])\*(?![\s*])(.+?)\*(?![\w*])/;

export function renderInline(text: string): ReactNode[] {
  const out: ReactNode[] = [];
  let rest = text;
  let key = 0;
  while (rest) {
    const m = rest.match(INLINE);
    if (!m || m.index === undefined) {
      out.push(rest);
      break;
    }
    if (m.index > 0) out.push(rest.slice(0, m.index));
    if (m[1] !== undefined) {
      out.push(
        <Box
          key={key++}
          component="code"
          sx={{ fontFamily: 'monospace', fontSize: '0.92em', bgcolor: 'action.hover', px: 0.5, py: 0.1, borderRadius: 0.5 }}
        >
          {m[1]}
        </Box>,
      );
    } else if (m[2] !== undefined) {
      out.push(<strong key={key++}>{renderInline(m[2])}</strong>);
    } else if (m[3] !== undefined) {
      out.push(
        <HelpLink key={key++} href={m[4]}>
          {renderInline(m[3])}
        </HelpLink>,
      );
    } else {
      out.push(<em key={key++}>{renderInline(m[5])}</em>);
    }
    rest = rest.slice(m.index + m[0].length);
  }
  return out;
}

function HelpLink({ href, children }: { href: string; children: ReactNode }) {
  if (href.startsWith('help:')) {
    const [topic, hash] = href.slice(5).split('#');
    return (
      <Link component={RouterLink} to={{ pathname: `/help/${topic}`, hash: hash ? `#${hash}` : '' }}>
        {children}
      </Link>
    );
  }
  if (href.startsWith('#')) {
    return (
      <Link component={RouterLink} to={{ hash: href }}>
        {children}
      </Link>
    );
  }
  if (href.startsWith('/')) {
    return (
      <Link component={RouterLink} to={href}>
        {children}
      </Link>
    );
  }
  return (
    <Link href={href} target="_blank" rel="noopener noreferrer">
      {children}
    </Link>
  );
}

// ---- blocks -------------------------------------------------------------------------------------

function HelpImage({ alt, src }: { alt: string; src: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const url = /^https?:/.test(src) ? src : helpImageUrl(src);

  return (
    <Box component="figure" sx={{ mx: 0, my: 2.5 }}>
      {url ? (
        <Box
          component="img"
          src={url}
          alt={alt}
          title={t('help.enlargeImage')}
          onClick={() => setOpen(true)}
          sx={{
            display: 'block',
            maxWidth: '100%',
            maxHeight: 560,
            borderRadius: 1,
            border: 1,
            borderColor: 'divider',
            boxShadow: 1,
            cursor: 'zoom-in',
          }}
        />
      ) : (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 1,
            minHeight: 120,
            border: 2,
            borderStyle: 'dashed',
            borderColor: 'divider',
            borderRadius: 1,
            color: 'text.secondary',
            px: 2,
          }}
        >
          <ImageOutlinedIcon />
          <Typography variant="body2">{t('help.screenshotComingSoon')}</Typography>
        </Box>
      )}
      {alt && (
        <Typography component="figcaption" variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
          {alt}
        </Typography>
      )}
      {url && (
        <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xl">
          <IconButton
            aria-label={t('common.actions.close')}
            onClick={() => setOpen(false)}
            sx={{ position: 'absolute', right: 8, top: 8, bgcolor: 'background.paper', '&:hover': { bgcolor: 'background.paper' } }}
          >
            <CloseIcon />
          </IconButton>
          <DialogContent sx={{ p: 1 }}>
            <Box component="img" src={url} alt={alt} sx={{ display: 'block', maxWidth: '100%' }} />
          </DialogContent>
        </Dialog>
      )}
    </Box>
  );
}

function Callout({ variant, blocks }: { variant: CalloutKind; blocks: Block[] }) {
  const { t } = useTranslation();
  if (variant === 'quote') {
    return (
      <Box sx={{ borderLeft: 4, borderColor: 'divider', pl: 2, my: 2, color: 'text.secondary' }}>
        <BlockList blocks={blocks} />
      </Box>
    );
  }
  const severity = variant === 'tip' ? 'success' : variant === 'warning' ? 'warning' : 'info';
  return (
    <Alert
      severity={severity}
      icon={variant === 'tip' ? <LightbulbOutlinedIcon fontSize="inherit" /> : undefined}
      sx={{ my: 2, '& p:last-child, & ul:last-child, & ol:last-child': { mb: 0 } }}
    >
      <AlertTitle>{t(`help.callout.${variant}`)}</AlertTitle>
      <BlockList blocks={blocks} />
    </Alert>
  );
}

function BlockView({ block }: { block: Block }) {
  switch (block.kind) {
    case 'heading': {
      const variant = block.level <= 1 ? 'h4' : block.level === 2 ? 'h5' : block.level === 3 ? 'h6' : 'subtitle1';
      return (
        <Typography
          id={block.id}
          variant={variant}
          component={`h${Math.min(block.level, 6)}` as 'h2'}
          sx={{ mt: block.level <= 2 ? 4 : 3, mb: 1.25, scrollMarginTop: 72, fontWeight: block.level >= 4 ? 600 : undefined }}
        >
          {renderInline(block.text)}
        </Typography>
      );
    }
    case 'paragraph':
      return (
        <Typography variant="body1" sx={{ mb: 1.5, lineHeight: 1.7 }}>
          {renderInline(block.text)}
        </Typography>
      );
    case 'list':
      return (
        <Box
          component={block.ordered ? 'ol' : 'ul'}
          start={block.ordered ? block.start : undefined}
          sx={{ pl: 3, mt: 0, mb: 1.5, '& > li': { mb: 0.5, lineHeight: 1.7 }, '& li > p': { mb: 0.5 } }}
        >
          {block.items.map((item, i) => (
            <Typography key={i} component="li" variant="body1">
              {item.length === 1 && item[0].kind === 'paragraph' ? renderInline(item[0].text) : <BlockList blocks={item} />}
            </Typography>
          ))}
        </Box>
      );
    case 'callout':
      return <Callout variant={block.variant} blocks={block.blocks} />;
    case 'table':
      return (
        <TableContainer component={Paper} variant="outlined" sx={{ my: 2 }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                {block.header.map((h, i) => (
                  <TableCell key={i} align={block.align[i]} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                    {renderInline(h)}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {block.rows.map((row, r) => (
                <TableRow key={r}>
                  {row.map((cell, c) => (
                    <TableCell key={c} align={block.align[c]}>
                      {renderInline(cell)}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      );
    case 'image':
      return <HelpImage alt={block.alt} src={block.src} />;
    case 'hr':
      return <Divider sx={{ my: 3 }} />;
  }
}

function BlockList({ blocks }: { blocks: Block[] }) {
  return (
    <>
      {blocks.map((b, i) => (
        <Fragment key={i}>
          <BlockView block={b} />
        </Fragment>
      ))}
    </>
  );
}

export function HelpMarkdown({ blocks }: { blocks: Block[] }) {
  return (
    <Box sx={{ '& > :first-child': { mt: 0 } }}>
      <BlockList blocks={blocks} />
    </Box>
  );
}
