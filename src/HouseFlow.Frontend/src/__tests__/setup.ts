import '@testing-library/jest-dom/vitest';

// Polyfill pointer capture APIs for Radix UI in jsdom
if (typeof HTMLElement !== 'undefined') {
  HTMLElement.prototype.hasPointerCapture = HTMLElement.prototype.hasPointerCapture || (() => false);
  HTMLElement.prototype.setPointerCapture = HTMLElement.prototype.setPointerCapture || (() => {});
  HTMLElement.prototype.releasePointerCapture = HTMLElement.prototype.releasePointerCapture || (() => {});
}

// Polyfill scrollIntoView for Radix UI
if (typeof Element !== 'undefined') {
  Element.prototype.scrollIntoView = Element.prototype.scrollIntoView || (() => {});
}
