import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * Bridges the `--gggs-color-*` custom properties (styles.scss) into PrimeNG's own token system, so
 * a single source of truth recolors hand-rolled markup and PrimeNG components alike - without this,
 * changing `--gggs-color-primary` (tenant branding) would silently do nothing to PrimeNG components.
 * `primary` is the GGGS brand red as a full scale (its 600 step is the brand red itself); the colours
 * buttons and highlights use go through the tokens, so the header's palette picker recolours them.
 */
export const GggsPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#fef0f1',
      100: '#fde0e2',
      200: '#fbc5c9',
      300: '#f79aa1',
      400: '#f0606b',
      500: '#e5323f',
      600: '#d81324',
      700: '#b00f1d',
      800: '#920f1b',
      900: '#79111b',
      950: '#42050a',
    },
    // Shape and focus follow the design tokens (styles/_tokens.scss in this library), so a PrimeNG dialog, table
    // or tag sits next to hand-rolled markup without looking like it came from another product.
    borderRadius: {
      none: '0',
      xs: 'var(--gggs-radius-xs)',
      sm: 'var(--gggs-radius-sm)',
      md: 'var(--gggs-radius-md)',
      lg: 'var(--gggs-radius-lg)',
      xl: 'var(--gggs-radius-xl)',
    },
    // Focus is charcoal, not the brand red, so a focused field never looks like an error.
    focusRing: {
      width: '2px',
      style: 'solid',
      color: 'var(--gggs-color-focus)',
      offset: '2px',
      shadow: 'none',
    },
    formField: {
      paddingX: 'var(--gggs-space-3)',
      paddingY: '0.4375rem',
      borderRadius: 'var(--gggs-radius-sm)',
      focusRing: {
        width: '0',
        style: 'none',
        color: 'transparent',
        offset: '0',
        shadow: 'var(--gggs-focus-ring)',
      },
    },
    colorScheme: {
      light: {
        primary: {
          color: 'var(--gggs-color-primary)',
          contrastColor: '#ffffff',
          hoverColor: 'var(--gggs-color-primary-hover)',
          // Through the token, not the scale, so the header's palette picker reaches pressed buttons too.
          activeColor: 'var(--gggs-color-primary-hover)',
        },
        highlight: {
          background: 'var(--gggs-color-primary-subtle)',
          focusBackground: 'var(--gggs-color-primary-subtle)',
          color: 'var(--gggs-color-primary)',
          focusColor: 'var(--gggs-color-primary)',
        },
        text: {
          color: 'var(--gggs-color-text)',
          hoverColor: 'var(--gggs-color-text)',
          mutedColor: 'var(--gggs-color-text-muted)',
          hoverMutedColor: 'var(--gggs-color-text-muted)',
        },
        content: {
          background: 'var(--gggs-color-surface-raised)',
          hoverBackground: 'var(--gggs-color-surface-hover)',
          borderColor: 'var(--gggs-color-border)',
        },
        formField: {
          background: 'var(--gggs-color-surface-raised)',
          borderColor: 'var(--gggs-color-border-strong)',
          hoverBorderColor: 'var(--gggs-color-text-subtle)',
          focusBorderColor: 'var(--gggs-color-focus)',
          color: 'var(--gggs-color-text)',
          placeholderColor: 'var(--gggs-color-text-subtle)',
        },
        mask: {
          background: 'var(--gggs-color-overlay)',
          color: 'var(--gggs-color-text)',
        },
        overlay: {
          modal: {
            background: 'var(--gggs-color-surface-raised)',
            borderColor: 'var(--gggs-color-border)',
            color: 'var(--gggs-color-text)',
          },
          popover: {
            background: 'var(--gggs-color-surface-raised)',
            borderColor: 'var(--gggs-color-border)',
            color: 'var(--gggs-color-text)',
          },
        },
      },
      dark: {
        primary: {
          color: 'var(--gggs-color-primary)',
          contrastColor: 'var(--gggs-color-primary-contrast)',
          hoverColor: 'var(--gggs-color-primary-hover)',
          // Through the token, not the scale, so the header's palette picker reaches pressed buttons too.
          activeColor: 'var(--gggs-color-primary-hover)',
        },
        highlight: {
          background: 'var(--gggs-color-primary-subtle)',
          focusBackground: 'var(--gggs-color-primary-subtle)',
          color: 'var(--gggs-color-primary)',
          focusColor: 'var(--gggs-color-primary)',
        },
        text: {
          color: 'var(--gggs-color-text)',
          hoverColor: 'var(--gggs-color-text)',
          mutedColor: 'var(--gggs-color-text-muted)',
          hoverMutedColor: 'var(--gggs-color-text-muted)',
        },
        content: {
          background: 'var(--gggs-color-surface-raised)',
          hoverBackground: 'var(--gggs-color-surface-hover)',
          borderColor: 'var(--gggs-color-border)',
        },
        formField: {
          background: 'var(--gggs-color-surface-raised)',
          borderColor: 'var(--gggs-color-border-strong)',
          hoverBorderColor: 'var(--gggs-color-text-subtle)',
          focusBorderColor: 'var(--gggs-color-focus)',
          color: 'var(--gggs-color-text)',
          placeholderColor: 'var(--gggs-color-text-subtle)',
        },
        mask: {
          background: 'var(--gggs-color-overlay)',
          color: 'var(--gggs-color-text)',
        },
        overlay: {
          modal: {
            background: 'var(--gggs-color-surface-raised)',
            borderColor: 'var(--gggs-color-border)',
            color: 'var(--gggs-color-text)',
          },
          popover: {
            background: 'var(--gggs-color-surface-raised)',
            borderColor: 'var(--gggs-color-border)',
            color: 'var(--gggs-color-text)',
          },
        },
      },
    },
  },
});
