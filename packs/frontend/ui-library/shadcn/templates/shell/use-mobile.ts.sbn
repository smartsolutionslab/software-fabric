import { useSyncExternalStore } from 'react';

const mobile = '(max-width: 767px)';

/** Whether the browser can answer media queries; jsdom cannot. */
function canQuery() {
  return typeof window.matchMedia === 'function';
}

function subscribe(onChange: () => void) {
  if (!canQuery()) {
    return () => undefined;
  }

  const query = window.matchMedia(mobile);
  query.addEventListener('change', onChange);
  return () => {
    query.removeEventListener('change', onChange);
  };
}

/** Whether the window is narrower than a tablet, where the sidebar opens as a sheet; false where the browser cannot say. */
export function useIsMobile() {
  return useSyncExternalStore(subscribe, () => canQuery() && window.matchMedia(mobile).matches);
}
