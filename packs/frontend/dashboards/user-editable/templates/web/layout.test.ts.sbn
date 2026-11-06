import { expect, test } from 'vitest';
import { applyLayout, hasRoom, lowest, nextPlacement, toLayout, withinReach } from './layout';

const chart = { key: 'a', source: 'dashboards.widget-counts', visual: 'bar', x: 0, y: 0, width: 6, height: 4 };
const rows = { key: 'b', source: 'dashboards.my-dashboards', visual: 'table', x: 6, y: 0, width: 6, height: 8 };
const placed = [chart, rows];

test('the grid layout is the placements, item by item', () => {
  expect(toLayout(placed)).toEqual([
    { i: 'a', x: 0, y: 0, w: 6, h: 4, maxH: 24 },
    { i: 'b', x: 6, y: 0, w: 6, h: 8, maxH: 24 },
  ]);
});

test('moving and resizing on the grid moves and resizes the placements it names', () => {
  const moved = applyLayout(placed, [
    { i: 'a', x: 6, y: 8, w: 4, h: 3 },
    { i: 'b', x: 0, y: 0, w: 12, h: 8 },
  ]);

  expect(moved).toEqual([
    { ...chart, x: 6, y: 8, width: 4, height: 3 },
    { ...rows, x: 0, y: 0, width: 12, height: 8 },
  ]);
});

test('a new widget goes below everything already on the grid, half as wide', () => {
  expect(nextPlacement(placed, 'c', 'dashboards.widget-counts', 'line')).toEqual({
    key: 'c',
    source: 'dashboards.widget-counts',
    visual: 'line',
    x: 0,
    y: 8,
    width: 6,
    height: 4,
  });
  expect(nextPlacement([], 'c', 'dashboards.widget-counts', 'pie').y).toBe(0);
});

test('the host takes no widget that starts below row 1000, so a new one fits only while the grid ends above it', () => {
  expect(lowest).toBe(1000);
  const full = [{ ...chart, y: 984, height: 24 }];

  expect(hasRoom(placed)).toBe(true);
  expect(hasRoom([{ ...chart, y: 996, height: 4 }])).toBe(true);
  expect(hasRoom(full)).toBe(false);
});

test('a layout is within reach only while every widget starts at row 1000 or above', () => {
  expect(withinReach(placed)).toBe(true);
  expect(withinReach([{ ...chart, y: 1000 }])).toBe(true);
  expect(withinReach([chart, { ...rows, y: 1001 }])).toBe(false);
});
