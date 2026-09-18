import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * Locale-aware value formatting (numbers, currency, percentages, dates) built on the Intl APIs.
 *
 * The formatting locale follows the *browser's* culture (i18next resolves `i18n.language` from
 * `navigator.language`), independent of which string bundle is active — so a de-DE browser sees
 * German grouping/decimal separators and date order even while the UI text falls back to en-US
 * (the only string bundle shipped today). Currency stays USD (the currency the catalog prices are
 * denominated in); only its *presentation* (symbol placement, separators) is localized, so an amount
 * is never silently reinterpreted as a different currency.
 */

const DEFAULT_LOCALE = 'en-US';

export function formatMoney(
  value: number | null | undefined,
  locale: string = DEFAULT_LOCALE,
  currency = 'USD',
): string {
  if (value == null || Number.isNaN(value)) return '';
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(value);
}

export function formatNumber(
  value: number | null | undefined,
  locale: string = DEFAULT_LOCALE,
  options?: Intl.NumberFormatOptions,
): string {
  if (value == null || Number.isNaN(value)) return '';
  return new Intl.NumberFormat(locale, options).format(value);
}

export function formatPercent(
  value: number | null | undefined,
  locale: string = DEFAULT_LOCALE,
  fractionDigits = 1,
): string {
  if (value == null || Number.isNaN(value)) return '';
  return new Intl.NumberFormat(locale, {
    style: 'percent',
    minimumFractionDigits: 0,
    maximumFractionDigits: fractionDigits,
  }).format(value);
}

function toDate(value: string | number | Date | null | undefined): Date | null {
  if (value == null || value === '') return null;
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function formatDate(
  value: string | number | Date | null | undefined,
  locale: string = DEFAULT_LOCALE,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium' },
): string {
  const d = toDate(value);
  return d ? new Intl.DateTimeFormat(locale, options).format(d) : '';
}

export function formatDateTime(
  value: string | number | Date | null | undefined,
  locale: string = DEFAULT_LOCALE,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' },
): string {
  const d = toDate(value);
  return d ? new Intl.DateTimeFormat(locale, options).format(d) : '';
}

/** Formatters bound to the active browser locale. Prefer this in components over the bare functions
 * so every value renders in one consistent culture. */
export function useFormatters() {
  const { i18n } = useTranslation();
  const locale = i18n.language || DEFAULT_LOCALE;
  return useMemo(
    () => ({
      locale,
      money: (value: number | null | undefined, currency?: string) => formatMoney(value, locale, currency),
      number: (value: number | null | undefined, options?: Intl.NumberFormatOptions) =>
        formatNumber(value, locale, options),
      percent: (value: number | null | undefined, fractionDigits?: number) =>
        formatPercent(value, locale, fractionDigits),
      date: (value: string | number | Date | null | undefined, options?: Intl.DateTimeFormatOptions) =>
        formatDate(value, locale, options),
      dateTime: (value: string | number | Date | null | undefined, options?: Intl.DateTimeFormatOptions) =>
        formatDateTime(value, locale, options),
    }),
    [locale],
  );
}
