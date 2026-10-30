import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

interface IconShape {
  paths: string[];
  circles?: [number, number, number][];
  rects?: [number, number, number, number, number][];
}

// Outline icons on a 24×24 grid (shapes adapted from Lucide, ISC licence).
const ICONS = {
  dashboard: { paths: [], rects: [[3, 3, 7, 9, 1], [14, 3, 7, 5, 1], [14, 12, 7, 9, 1], [3, 16, 7, 5, 1]] },
  cart: {
    paths: ['M2.05 2.05h2l2.66 12.42a2 2 0 0 0 2 1.58h9.78a2 2 0 0 0 1.95-1.57l1.65-7.43H5.12'],
    circles: [[8, 21, 1], [19, 21, 1]],
  },
  package: {
    paths: [
      'M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z',
      'M12 22V12',
      'm3.3 7 7.7 4.73a2 2 0 0 0 2 0L20.7 7',
      'm7.5 4.27 9 5.15',
    ],
  },
  layers: {
    paths: [
      'M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z',
      'm22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65',
      'm22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65',
    ],
  },
  receipt: {
    paths: [
      'M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1Z',
      'M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8',
      'M12 17.5v-11',
    ],
  },
  chart: { paths: ['M3 3v18h18', 'M18 17V9', 'M13 17V5', 'M8 17v-3'] },
  users: {
    paths: ['M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2', 'M22 21v-2a4 4 0 0 0-3-3.87', 'M16 3.13a4 4 0 0 1 0 7.75'],
    circles: [[9, 7, 4]],
  },
  user: {
    paths: ['M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2'],
    circles: [[12, 7, 4]],
  },
  logout: { paths: ['M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4', 'm16 17 5-5-5-5', 'M21 12H9'] },
  menu: { paths: ['M4 6h16', 'M4 12h16', 'M4 18h16'] },
  search: { paths: ['m21 21-4.3-4.3'], circles: [[11, 11, 8]] },
  plus: { paths: ['M5 12h14', 'M12 5v14'] },
  minus: { paths: ['M5 12h14'] },
  edit: {
    paths: [
      'M21.17 6.81a1 1 0 0 0-3.99-3.99L3.84 16.17a2 2 0 0 0-.5.83l-1.32 4.35a.5.5 0 0 0 .62.62l4.35-1.32a2 2 0 0 0 .83-.5z',
      'm15 5 4 4',
    ],
  },
  trash: { paths: ['M3 6h18', 'M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6', 'M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2'] },
  close: { paths: ['M18 6 6 18', 'm6 6 12 12'] },
  alert: { paths: ['M12 8v4', 'M12 16h.01'], circles: [[12, 12, 10]] },
  warning: {
    paths: ['m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3', 'M12 9v4', 'M12 17h.01'],
  },
  check: { paths: ['M20 6 9 17l-5-5'] },
  printer: {
    paths: ['M6 9V2h12v7', 'M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2'],
    rects: [[6, 14, 12, 8, 0]],
  },
  refresh: {
    paths: [
      'M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8',
      'M21 3v5h-5',
      'M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16',
      'M8 16H3v5',
    ],
  },
  chevronLeft: { paths: ['m15 18-6-6 6-6'] },
  chevronRight: { paths: ['m9 18 6-6-6-6'] },
  history: { paths: ['M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8', 'M3 3v5h5', 'M12 7v5l4 2'] },
  trend: { paths: ['M22 7 13.5 15.5 8.5 10.5 2 17', 'M16 7h6v6'] },
  banknote: { paths: ['M6 12h.01', 'M18 12h.01'], circles: [[12, 12, 2]], rects: [[2, 6, 20, 12, 2]] },
  eye: {
    paths: ['M2.06 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.88 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.88 0'],
    circles: [[12, 12, 3]],
  },
  sparkles: {
    paths: [
      'M9.94 15.5A2 2 0 0 0 8.5 14.06l-6.14-1.58a.5.5 0 0 1 0-.96L8.5 9.94A2 2 0 0 0 9.94 8.5l1.58-6.14a.5.5 0 0 1 .96 0l1.58 6.14a2 2 0 0 0 1.44 1.44l6.14 1.58a.5.5 0 0 1 0 .96l-6.14 1.58a2 2 0 0 0-1.44 1.44l-1.58 6.14a.5.5 0 0 1-.96 0z',
      'M20 3v4',
      'M22 5h-4',
    ],
  },
  send: {
    paths: [
      'M14.54 21.69a.5.5 0 0 0 .94-.03l6.5-19a.5.5 0 0 0-.64-.63l-19 6.5a.5.5 0 0 0-.02.93l7.93 3.18a2 2 0 0 1 1.11 1.11z',
      'm21.85 2.15-10.94 10.94',
    ],
  },
} satisfies Record<string, IconShape>;

export type IconName = keyof typeof ICONS;

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', style: 'display:inline-flex;flex-shrink:0' },
  template: `
    <svg
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 24 24"
      [attr.width]="size()"
      [attr.height]="size()"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      @for (d of shape().paths; track $index) {
        <path [attr.d]="d" />
      }
      @for (c of shape().circles ?? []; track $index) {
        <circle [attr.cx]="c[0]" [attr.cy]="c[1]" [attr.r]="c[2]" />
      }
      @for (r of shape().rects ?? []; track $index) {
        <rect [attr.x]="r[0]" [attr.y]="r[1]" [attr.width]="r[2]" [attr.height]="r[3]" [attr.rx]="r[4]" />
      }
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(18);
  protected readonly shape = computed<IconShape>(() => ICONS[this.name()]);
}
