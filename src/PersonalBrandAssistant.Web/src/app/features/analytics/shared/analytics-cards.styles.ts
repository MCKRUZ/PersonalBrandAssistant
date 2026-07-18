// Shared obsidian-theme styles for analytics card/panel/table/skeleton primitives.
// Included in each analytics component's `styles` array (view-encapsulated per component,
// so this is the single edit point for the shared visual vocabulary).
export const ANALYTICS_CARD_STYLES = `
  :host { display: block; }

  .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; margin-bottom: 24px; flex-wrap: wrap; }
  .page-title { font-family: var(--font-display); font-size: 28px; line-height: 1.1; color: var(--text-primary); margin: 0; }
  .page-sub { margin: 6px 0 0; color: var(--text-secondary); font-size: 13px; }

  .kpi-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(168px, 1fr)); gap: 14px; margin-bottom: 18px; }
  .kpi-card { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 16px 18px; display: flex; flex-direction: column; gap: 8px; }
  .kpi-label { font-size: 11px; letter-spacing: .05em; text-transform: uppercase; color: var(--text-secondary); }
  .kpi-value { font-family: var(--font-mono); font-size: 27px; font-weight: 600; color: var(--text-primary); font-variant-numeric: tabular-nums; }
  .kpi-delta { font-family: var(--font-mono); font-size: 12px; }
  .kpi-delta.up { color: var(--status-approved, #4ade80); }
  .kpi-delta.down { color: var(--status-rejected, #f0935f); }

  .panel { background: var(--surface-card); border: 1px solid var(--surface-border); border-radius: var(--r); padding: 18px 20px; }
  .panel-head { display: flex; align-items: center; gap: 7px; margin-bottom: 14px; }
  .panel-head h2 { font-family: var(--font-display); font-size: 17px; font-weight: 400; color: var(--text-primary); margin: 0; }

  .data-table { width: 100%; border-collapse: collapse; font-size: 13px; }
  .data-table th { text-align: left; font-weight: 500; color: var(--text-secondary); font-size: 11px; letter-spacing: .04em; text-transform: uppercase; padding: 8px 12px; border-bottom: 1px solid var(--surface-border); }
  .data-table td { padding: 10px 12px; border-bottom: 1px solid color-mix(in srgb, var(--surface-border) 55%, transparent); color: var(--text-primary); }
  .data-table tbody tr:last-child td { border-bottom: 0; }
  .num { text-align: right; font-family: var(--font-mono); font-variant-numeric: tabular-nums; white-space: nowrap; }
  th.num { text-align: right; }

  .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 80px 16px; color: var(--text-muted); }
  .empty i { font-size: 26px; }
  .empty p { margin: 0; font-size: 13px; }

  .sk { background: linear-gradient(90deg, var(--surface-hover) 25%, var(--surface-elevated) 50%, var(--surface-hover) 75%); background-size: 200% 100%; animation: sk-shimmer 1.3s infinite; border-radius: var(--r-control); }
  .sk-line { height: 11px; width: 60%; margin-bottom: 8px; }
  .sk-value { height: 26px; width: 80%; }
  .skeleton-card { padding: 16px 18px; }
  @keyframes sk-shimmer { 0% { background-position: 200% 0; } 100% { background-position: -200% 0; } }
`;
