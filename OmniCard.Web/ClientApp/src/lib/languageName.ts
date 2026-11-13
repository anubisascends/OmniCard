import i18next, { type TFunction } from 'i18next';

/** The browser's own name for a language code in the UI language, when it knows one. */
function browserLanguageName(code: string): string | undefined {
  try {
    const name = new Intl.DisplayNames([i18next.language || 'en-US'], { type: 'language' }).of(code);
    return name && name.toLowerCase() !== code.toLowerCase() ? name : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Localized display name for a card-language code. A code with no translation (a language the server
 * found in a catalog source after this build) uses the browser's name for it, else the upper-cased code.
 */
export function languageName(t: TFunction, code: string): string {
  return t(`common.languages.${code}`, { defaultValue: browserLanguageName(code) ?? code.toUpperCase() });
}
