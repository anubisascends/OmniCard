import { FALLBACK_LNG } from '../i18n';

// In-app help content. Each topic is one Markdown file per culture (`./<culture>/<topic-id>.md`), so
// help translates the same way the string bundles do: drop a `./de-DE/` folder of translated topics
// and it's picked up — missing topics fall back to en-US. Screenshots live in `./images/` and are
// shared by every culture. Bundled at build time (no runtime fetch), lazily with the Help page chunk.
const markdownFiles = import.meta.glob('./*/*.md', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const imageFiles = import.meta.glob('./images/*.{png,jpg,jpeg,webp,gif}', {
  query: '?url',
  import: 'default',
  eager: true,
}) as Record<string, string>;

/** Sidebar grouping + reading order. `key` resolves against `help.groups.*`. */
export const HELP_GROUPS: { key: string; topics: string[] }[] = [
  { key: 'start', topics: ['getting-started', 'dashboard'] },
  { key: 'scanning', topics: ['scanning', 'scan-batches', 'location-audit'] },
  { key: 'collection', topics: ['collection', 'saved-views', 'search-syntax', 'sets'] },
  { key: 'organizing', topics: ['locations', 'binders', 'deck-boxes'] },
  { key: 'lists', topics: ['lists', 'importing', 'trades'] },
  { key: 'selling', topics: ['inventory', 'sales', 'ebay'] },
  { key: 'admin', topics: ['administration', 'troubleshooting'] },
];

export const HELP_TOPIC_ORDER = HELP_GROUPS.flatMap((g) => g.topics);

export interface HelpTopic {
  id: string;
  title: string;
  summary: string;
  /** Markdown body without the leading `# Title` line. */
  body: string;
  /** Title + body with Markdown markers stripped (original casing) — search snippets come from here. */
  plainText: string;
  /** `plainText` lower-cased, for matching. */
  searchText: string;
}

function cultureFolders(): string[] {
  const set = new Set<string>();
  for (const path of Object.keys(markdownFiles)) set.add(path.split('/')[1]);
  return [...set];
}

/** Culture folders to try for `language`, most specific first, ending with the en-US fallback. */
function candidateFolders(language: string | undefined): string[] {
  const folders = cultureFolders();
  const out: string[] = [];
  const add = (f: string | undefined) => {
    if (f && !out.includes(f)) out.push(f);
  };
  if (language) {
    add(folders.find((f) => f.toLowerCase() === language.toLowerCase()));
    const base = language.split('-')[0].toLowerCase();
    add(folders.find((f) => f.toLowerCase() === base));
    add(folders.find((f) => f.toLowerCase().split('-')[0] === base));
  }
  add(FALLBACK_LNG);
  return out;
}

function parseTopic(id: string, raw: string): HelpTopic {
  const lines = raw.replace(/\r\n?/g, '\n').split('\n');
  let title = id;
  const h1 = lines.findIndex((l) => /^#\s+/.test(l));
  if (h1 >= 0) {
    title = lines[h1].replace(/^#\s+/, '').trim();
    lines.splice(h1, 1);
  }
  const body = lines.join('\n').trim();
  // Summary = first plain paragraph (skips images/headings/callouts).
  const summary =
    body
      .split(/\n\s*\n/)
      .map((p) => p.trim())
      .find((p) => p && !/^(#|!\[|>|-|\d+\.|\|)/.test(p))
      ?.replace(/\s+/g, ' ') ?? '';
  const plainText = stripInline(`${title}\n${body}`)
    .replace(/^\s*(#+|\||>|-|\d+\.)\s*/gm, '')
    .replace(/\||:?-{3,}:?/g, ' ')
    .replace(/\s+/g, ' ');
  return { id, title, summary: stripInline(summary), body, plainText, searchText: plainText.toLowerCase() };
}

/** Removes inline Markdown markers so text can be searched / shown as a snippet. */
export function stripInline(text: string): string {
  return text
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/\*\*|`|^>\s*\[![A-Z]+\]/gm, '')
    .replace(/(^|\s)\*([^*]+)\*/g, '$1$2');
}

const cache = new Map<string, HelpTopic[]>();

/** All topics for `language` in reading order (each falls back to en-US individually). */
export function getHelpTopics(language: string | undefined): HelpTopic[] {
  const key = language ?? '';
  const hit = cache.get(key);
  if (hit) return hit;
  const folders = candidateFolders(language);
  const topics: HelpTopic[] = [];
  for (const id of HELP_TOPIC_ORDER) {
    const folder = folders.find((f) => markdownFiles[`./${f}/${id}.md`] !== undefined);
    if (folder) topics.push(parseTopic(id, markdownFiles[`./${folder}/${id}.md`]));
  }
  cache.set(key, topics);
  return topics;
}

/** Resolves a screenshot file name (as written in the Markdown) to its bundled URL, if it exists. */
export function helpImageUrl(fileName: string): string | undefined {
  return imageFiles[`./images/${fileName.replace(/^\.?\/?(images\/)?/, '')}`];
}

/** Heading text → anchor id (matches the authoring convention: lower-case, non-alphanumerics → '-'). */
export function slugify(text: string): string {
  return stripInline(text)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
}

/** Case-insensitive multi-word search; every word must appear. Returns topics with a context snippet. */
export function searchHelp(topics: HelpTopic[], query: string): { topic: HelpTopic; snippet: string }[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (words.length === 0) return [];
  return topics
    .filter((t) => words.every((w) => t.searchText.includes(w)))
    .map((t) => {
      const titleHit = words.every((w) => t.title.toLowerCase().includes(w));
      return { topic: t, snippet: snippetFor(t, words[0]), score: titleHit ? 0 : 1 };
    })
    .sort((a, b) => a.score - b.score)
    .map(({ topic, snippet }) => ({ topic, snippet }));
}

function snippetFor(topic: HelpTopic, word: string): string {
  const text = topic.plainText;
  const at = topic.searchText.indexOf(word);
  if (at < 0) return topic.summary;
  const start = Math.max(0, at - 60);
  const end = Math.min(text.length, at + word.length + 90);
  return `${start > 0 ? '…' : ''}${text.slice(start, end).trim()}${end < text.length ? '…' : ''}`;
}
