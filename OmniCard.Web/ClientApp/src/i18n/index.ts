import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';

// en-US string bundle, assembled from one file per feature area. Each file is keyed by a unique
// top-level namespace object (e.g. { "binder": { … } }), so the spreads below merge without
// collisions — and new files/areas just add another import + spread. New *languages* drop in later
// as a sibling folder (e.g. locales/de-DE/*) added to `resources`; no component changes needed.
import common from './locales/en-US/common.json';
import nav from './locales/en-US/nav.json';
import auth from './locales/en-US/auth.json';
import dashboard from './locales/en-US/dashboard.json';
import collection from './locales/en-US/collection.json';
import locations from './locales/en-US/locations.json';
import binder from './locales/en-US/binder.json';
import sets from './locales/en-US/sets.json';
import scan from './locales/en-US/scan.json';
import sales from './locales/en-US/sales.json';
import inventory from './locales/en-US/inventory.json';
import importing from './locales/en-US/importing.json';
import lists from './locales/en-US/lists.json';
import trades from './locales/en-US/trades.json';
import settings from './locales/en-US/settings.json';
import deckbox from './locales/en-US/deckbox.json';
import dialogs from './locales/en-US/dialogs.json';
import search from './locales/en-US/search.json';

const enUS = {
  ...common,
  ...nav,
  ...auth,
  ...dashboard,
  ...collection,
  ...locations,
  ...binder,
  ...sets,
  ...scan,
  ...sales,
  ...inventory,
  ...importing,
  ...lists,
  ...trades,
  ...settings,
  ...deckbox,
  ...dialogs,
  ...search,
};

export const FALLBACK_LNG = 'en-US';

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { 'en-US': { translation: enUS } },
    // Missing keys / unshipped languages resolve to en-US, but `i18n.language` keeps the browser's
    // real culture (e.g. de-DE) so value formatting via ./format stays locale-correct.
    fallbackLng: FALLBACK_LNG,
    interpolation: { escapeValue: false }, // React already escapes
    detection: {
      // Follow the browser culture; `?lng=` is a manual override for testing. No cache, so it always
      // tracks the browser rather than pinning the first-seen language.
      order: ['querystring', 'navigator', 'htmlTag'],
      lookupQuerystring: 'lng',
      caches: [],
    },
  });

export default i18n;
