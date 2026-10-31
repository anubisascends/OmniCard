import { useEffect, useRef, type MouseEvent, type TouchEvent } from 'react';
import { useMediaQuery, useTheme } from '@mui/material';

/** Whether the viewport is phone-sized (below the theme's `sm` breakpoint): pages switch to their compact layouts. */
export function useIsPhone(): boolean {
  const theme = useTheme();
  return useMediaQuery(theme.breakpoints.down('sm'));
}

/** Whether the primary pointer can hover (a mouse). Touch screens can't, so hover-only UI needs another way in. */
export function useCanHover(): boolean {
  return useMediaQuery('(hover: hover)', { noSsr: true });
}

/** Props that keep a long-press from selecting text or opening the browser's image callout. */
export const LONG_PRESS_SX = { WebkitTouchCallout: 'none', userSelect: 'none' } as const;

/**
 * Touch long-press: `const longPress = useLongPress();` then spread `{...longPress((el) => …)}` onto any
 * number of elements. Holding a finger still for `ms` fires the callback with the pressed element; the
 * tap that ends the press is swallowed (so a row doesn't also open), as is the browser's context menu.
 * Moving the finger (scrolling) cancels it. Mouse input is unaffected.
 */
export function useLongPress(ms = 450) {
  const timer = useRef<number | undefined>(undefined);
  const origin = useRef<{ x: number; y: number } | null>(null);
  const fired = useRef(false);
  const cancel = () => {
    window.clearTimeout(timer.current);
    timer.current = undefined;
  };
  useEffect(() => cancel, []);

  return (onLongPress: (el: HTMLElement) => void) => ({
    onTouchStart: (e: TouchEvent<HTMLElement>) => {
      cancel();
      fired.current = false;
      const touch = e.touches[0];
      origin.current = { x: touch.clientX, y: touch.clientY };
      const el = e.currentTarget;
      timer.current = window.setTimeout(() => {
        timer.current = undefined;
        fired.current = true;
        navigator.vibrate?.(10);
        onLongPress(el);
      }, ms);
    },
    onTouchMove: (e: TouchEvent<HTMLElement>) => {
      const touch = e.touches[0];
      if (origin.current && Math.hypot(touch.clientX - origin.current.x, touch.clientY - origin.current.y) > 10) cancel();
    },
    onTouchEnd: (e: TouchEvent<HTMLElement>) => {
      cancel();
      // Cancelling the touchend stops the browser from turning it into a click.
      if (fired.current && e.cancelable) e.preventDefault();
    },
    onTouchCancel: cancel,
    onContextMenu: (e: MouseEvent<HTMLElement>) => {
      if (fired.current || timer.current !== undefined) e.preventDefault();
    },
  });
}
