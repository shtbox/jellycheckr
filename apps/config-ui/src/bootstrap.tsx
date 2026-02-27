import { render } from 'preact';

import { PAGE_ID, ROOT_ID } from './constants';
import { defaults } from './configSchema';
import { App } from './components/App';
import { dirty, loadConfig, loading, snapshot } from './store';

const DEFAULT_SNAPSHOT = JSON.stringify(defaults);

export function bind(): void {
  const page = document.getElementById(PAGE_ID);
  const root = document.getElementById(ROOT_ID);

  if (!root) return;

  if (!(root as any).__jellycheckrMounted) {
    render(<App />, root);
    (root as any).__jellycheckrMounted = true;
  }

  if (page && !(page as any).__jellycheckrPageBound) {
    (page as any).__jellycheckrPageBound = true;
    page.addEventListener('pageshow', () => {
      void loadConfig();
    });
  }

  if (!loading.value && snapshot.value === DEFAULT_SNAPSHOT && !dirty.value) {
    void loadConfig();
  }
}
