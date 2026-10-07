import { useEffect, useMemo, useState } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  Alert,
  Box,
  Button,
  Card,
  CardActionArea,
  CardContent,
  InputAdornment,
  Link,
  List,
  ListItemButton,
  ListItemText,
  ListSubheader,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { useTranslation } from 'react-i18next';
import { getHelpTopics, HELP_GROUPS, searchHelp, type HelpTopic } from '../help/content';
import { HelpMarkdown, parseBlocks } from '../help/HelpMarkdown';

/** In-app help: `/help` is the topic index (+ search), `/help/:topicId` renders one topic. */
export function HelpPage() {
  const { t, i18n } = useTranslation();
  const { topicId } = useParams();
  const topics = useMemo(() => getHelpTopics(i18n.language), [i18n.language]);
  const byId = useMemo(() => new Map(topics.map((tp) => [tp.id, tp])), [topics]);
  const [query, setQuery] = useState('');

  if (!topicId) return <HelpHome topics={topics} byId={byId} query={query} setQuery={setQuery} />;

  const topic = byId.get(topicId);
  return (
    <Box sx={{ display: 'flex', gap: 3, alignItems: 'flex-start' }}>
      <Box sx={{ display: { xs: 'none', md: 'block' }, width: 240, flexShrink: 0, position: 'sticky', top: 64 }}>
        <TopicNav topics={topics} byId={byId} current={topicId} query={query} setQuery={setQuery} />
      </Box>
      <Box sx={{ flex: 1, minWidth: 0 }}>
        <Button
          component={RouterLink}
          to="/help"
          size="small"
          startIcon={<ArrowBackIcon />}
          sx={{ display: { md: 'none' }, mb: 1 }}
        >
          {t('help.allTopics')}
        </Button>
        {topic ? (
          <TopicView topic={topic} topics={topics} />
        ) : (
          <Alert severity="warning" action={<Button component={RouterLink} to="/help">{t('help.allTopics')}</Button>}>
            {t('help.topicNotFound')}
          </Alert>
        )}
      </Box>
    </Box>
  );
}

function SearchField({ query, setQuery, autoFocus }: { query: string; setQuery: (q: string) => void; autoFocus?: boolean }) {
  const { t } = useTranslation();
  return (
    <TextField
      size="small"
      fullWidth
      autoFocus={autoFocus}
      placeholder={t('help.searchPlaceholder')}
      value={query}
      onChange={(e) => setQuery(e.target.value)}
      slotProps={{
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" />
            </InputAdornment>
          ),
        },
      }}
    />
  );
}

function HelpHome({
  topics,
  byId,
  query,
  setQuery,
}: {
  topics: HelpTopic[];
  byId: Map<string, HelpTopic>;
  query: string;
  setQuery: (q: string) => void;
}) {
  const { t } = useTranslation();
  const results = useMemo(() => searchHelp(topics, query), [topics, query]);
  const searching = query.trim().length > 0;

  return (
    <Stack spacing={3} sx={{ maxWidth: 1100 }}>
      <Box>
        <Typography variant="h4" gutterBottom>
          {t('help.title')}
        </Typography>
        <Typography color="text.secondary">{t('help.subtitle')}</Typography>
      </Box>
      <Box sx={{ maxWidth: 560 }}>
        <SearchField query={query} setQuery={setQuery} autoFocus />
      </Box>

      {searching ? (
        <Stack spacing={1.5}>
          <Typography variant="overline" color="text.secondary">
            {t('help.resultCount', { count: results.length })}
          </Typography>
          {results.length === 0 && <Alert severity="info">{t('help.noResults', { query: query.trim() })}</Alert>}
          {results.map(({ topic, snippet }) => (
            <Card key={topic.id} variant="outlined">
              <CardActionArea component={RouterLink} to={`/help/${topic.id}`}>
                <CardContent>
                  <Typography variant="h6">{topic.title}</Typography>
                  <Typography variant="body2" color="text.secondary">
                    {snippet}
                  </Typography>
                </CardContent>
              </CardActionArea>
            </Card>
          ))}
        </Stack>
      ) : (
        <>
          {byId.has('getting-started') && (
            <Alert
              severity="info"
              action={
                <Button component={RouterLink} to="/help/getting-started" endIcon={<ArrowForwardIcon />}>
                  {t('help.startHereAction')}
                </Button>
              }
            >
              {t('help.startHere')}
            </Alert>
          )}
          <Box
            sx={{
              display: 'grid',
              gap: 2,
              gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' },
            }}
          >
            {HELP_GROUPS.map((group) => {
              const groupTopics = group.topics.map((id) => byId.get(id)).filter((tp): tp is HelpTopic => !!tp);
              if (groupTopics.length === 0) return null;
              return (
                <Paper key={group.key} variant="outlined" sx={{ p: 2 }}>
                  <Typography variant="overline" color="primary" sx={{ fontWeight: 600 }}>
                    {t(`help.groups.${group.key}`)}
                  </Typography>
                  <Stack spacing={1.5} sx={{ mt: 0.5 }}>
                    {groupTopics.map((tp) => (
                      <Box key={tp.id}>
                        <Link component={RouterLink} to={`/help/${tp.id}`} variant="subtitle1" underline="hover" sx={{ fontWeight: 600 }}>
                          {tp.title}
                        </Link>
                        <Typography variant="body2" color="text.secondary">
                          {tp.summary}
                        </Typography>
                      </Box>
                    ))}
                  </Stack>
                </Paper>
              );
            })}
          </Box>
        </>
      )}
    </Stack>
  );
}

function TopicNav({
  topics,
  byId,
  current,
  query,
  setQuery,
}: {
  topics: HelpTopic[];
  byId: Map<string, HelpTopic>;
  current: string;
  query: string;
  setQuery: (q: string) => void;
}) {
  const { t } = useTranslation();
  const matches = useMemo(
    () => (query.trim() ? new Set(searchHelp(topics, query).map((r) => r.topic.id)) : undefined),
    [topics, query],
  );

  return (
    <Paper variant="outlined" sx={{ maxHeight: 'calc(100vh - 88px)', overflowY: 'auto' }}>
      <Box sx={{ p: 1.5, pb: 0.5 }}>
        <SearchField query={query} setQuery={setQuery} />
      </Box>
      <List dense disablePadding>
        <ListItemButton component={RouterLink} to="/help">
          <ListItemText primary={t('help.allTopics')} slotProps={{ primary: { color: 'primary', fontWeight: 600 } }} />
        </ListItemButton>
        {HELP_GROUPS.map((group) => {
          const groupTopics = group.topics
            .map((id) => byId.get(id))
            .filter((tp): tp is HelpTopic => !!tp && (!matches || matches.has(tp.id)));
          if (groupTopics.length === 0) return null;
          return (
            <li key={group.key}>
              <ul style={{ padding: 0 }}>
                <ListSubheader sx={{ lineHeight: '32px', bgcolor: 'background.paper' }}>{t(`help.groups.${group.key}`)}</ListSubheader>
                {groupTopics.map((tp) => (
                  <ListItemButton key={tp.id} component={RouterLink} to={`/help/${tp.id}`} selected={tp.id === current} sx={{ pl: 3 }}>
                    <ListItemText primary={tp.title} />
                  </ListItemButton>
                ))}
              </ul>
            </li>
          );
        })}
        {matches && matches.size === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ px: 2, py: 1 }}>
            {t('help.noResults', { query: query.trim() })}
          </Typography>
        )}
      </List>
    </Paper>
  );
}

function TopicView({ topic, topics }: { topic: HelpTopic; topics: HelpTopic[] }) {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const blocks = useMemo(() => parseBlocks(topic.body), [topic]);
  const sections = blocks.filter((b): b is Extract<typeof b, { kind: 'heading' }> => b.kind === 'heading' && b.level === 2);
  const index = topics.findIndex((tp) => tp.id === topic.id);
  const prev = index > 0 ? topics[index - 1] : undefined;
  const next = index >= 0 && index < topics.length - 1 ? topics[index + 1] : undefined;

  // Scroll to `#anchor` (cross-topic links and the "On this page" list), or to the top on a new topic.
  // Screenshots above the target shift the layout as they load, so scroll again once they've decoded.
  useEffect(() => {
    const id = decodeURIComponent(location.hash.replace(/^#/, ''));
    if (!id) {
      window.scrollTo({ top: 0 });
      return;
    }
    let cancelled = false;
    const scroll = () => {
      if (!cancelled) document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    };
    scroll();
    const pending = [...document.querySelectorAll<HTMLImageElement>('article img')].filter((img) => !img.complete);
    if (pending.length) void Promise.all(pending.map((img) => img.decode().catch(() => undefined))).then(scroll);
    return () => {
      cancelled = true;
    };
  }, [location.hash, topic.id]);

  return (
    <Box sx={{ display: 'flex', gap: 4, alignItems: 'flex-start' }}>
      <Box component="article" sx={{ flex: 1, minWidth: 0, maxWidth: 880 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          {topic.title}
        </Typography>
        <HelpMarkdown blocks={blocks} />

        <Stack direction="row" spacing={2} sx={{ mt: 5, pt: 2, borderTop: 1, borderColor: 'divider' }} justifyContent="space-between">
          {prev ? (
            <Button startIcon={<ArrowBackIcon />} onClick={() => navigate(`/help/${prev.id}`)} sx={{ textAlign: 'left' }}>
              <Box>
                <Typography variant="caption" display="block" color="text.secondary">
                  {t('help.previous')}
                </Typography>
                {prev.title}
              </Box>
            </Button>
          ) : (
            <span />
          )}
          {next && (
            <Button endIcon={<ArrowForwardIcon />} onClick={() => navigate(`/help/${next.id}`)} sx={{ textAlign: 'right' }}>
              <Box>
                <Typography variant="caption" display="block" color="text.secondary">
                  {t('help.next')}
                </Typography>
                {next.title}
              </Box>
            </Button>
          )}
        </Stack>
      </Box>

      {sections.length > 1 && (
        <Box component="nav" sx={{ display: { xs: 'none', lg: 'block' }, width: 220, flexShrink: 0, position: 'sticky', top: 64 }}>
          <Typography variant="overline" color="text.secondary">
            {t('help.onThisPage')}
          </Typography>
          <Stack spacing={0.75} sx={{ mt: 0.5, borderLeft: 2, borderColor: 'divider', pl: 1.5 }}>
            {sections.map((s) => (
              <Link
                key={s.id}
                component={RouterLink}
                to={{ hash: `#${s.id}` }}
                variant="body2"
                underline="hover"
                color={location.hash === `#${s.id}` ? 'primary' : 'text.secondary'}
              >
                {s.text.replace(/\*\*|`/g, '')}
              </Link>
            ))}
          </Stack>
        </Box>
      )}
    </Box>
  );
}
