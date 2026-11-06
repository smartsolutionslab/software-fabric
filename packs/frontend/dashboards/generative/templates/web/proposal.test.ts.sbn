import { expect, test } from 'vitest';
import type { Placement } from './layout';
import { acceptProposal, draftState, readProposal } from './proposal';

const counts: Placement = {
  key: 'counts',
  source: 'dashboards.widget-counts',
  visual: 'bar',
  x: 6,
  y: 2,
  width: 4,
  height: 3,
};

test("a propose_widget result is the host's proposal, and anything else is none", () => {
  expect(
    readProposal(
      '{"source":"dashboards.widget-counts","title":"Widgets per dashboard","visual":"pie","replaces":"counts"}',
    ),
  ).toEqual({ source: 'dashboards.widget-counts', title: 'Widgets per dashboard', visual: 'pie', replaces: 'counts' });
  expect(readProposal('{"source":"a.b","title":"B","visual":"bar","replaces":null}')).toEqual({
    source: 'a.b',
    title: 'B',
    visual: 'bar',
    replaces: null,
  });

  expect(
    readProposal('Failed: dashboards.unknown-widget: No widget nothing.declared is declared for you.'),
  ).toBeUndefined();
  expect(readProposal('{"source":"a.b","visual":"bar","replaces":null}')).toBeUndefined();
  expect(readProposal('{"source":1,"title":"B","visual":"bar","replaces":null}')).toBeUndefined();
  expect(readProposal('[]')).toBeUndefined();
  expect(readProposal('null')).toBeUndefined();
});

test('an accepted proposal goes below the widgets on the grid, or takes the place of the widget it replaces', () => {
  const added = acceptProposal([counts], { source: 'a.b', title: 'B', visual: 'line', replaces: null }, 'new');
  expect(added).toEqual([counts, { key: 'new', source: 'a.b', visual: 'line', x: 0, y: 5, width: 6, height: 4 }]);

  const replaced = acceptProposal([counts], { source: 'a.b', title: 'B', visual: 'pie', replaces: 'counts' }, 'new');
  expect(replaced).toEqual([{ key: 'new', source: 'a.b', visual: 'pie', x: 6, y: 2, width: 4, height: 3 }]);
});

test('a proposal replacing a widget the draft no longer has is added instead', () => {
  const added = acceptProposal([counts], { source: 'a.b', title: 'B', visual: 'bar', replaces: 'gone' }, 'new');

  expect(added).toEqual([counts, { key: 'new', source: 'a.b', visual: 'bar', x: 0, y: 5, width: 6, height: 4 }]);
});

test("the run's state is the draft's widgets by key, source and visual, without their places", () => {
  expect(draftState([counts])).toEqual({
    widgets: [{ key: 'counts', source: 'dashboards.widget-counts', visual: 'bar' }],
  });
  expect(draftState([])).toEqual({ widgets: [] });
});
