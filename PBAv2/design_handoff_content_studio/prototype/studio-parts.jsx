// ===== PBAv2 Content Studio — UI parts =====
const { useState, useRef } = React;

// ---------- App shell sidebar ----------
const NAV = [
  { label: 'Feed', icon: '⌂' },
  { label: 'Discover', icon: '◎' },
  { label: 'Ideas', icon: '◈' },
  { label: 'Create', icon: '✎', active: true },
  { label: 'Calendar', icon: '▦' },
  { label: 'Analytics', icon: '◧' },
  { label: 'Listening', icon: '◉' },
  { label: 'Settings', icon: '⚙' },
];

function AppSidebar() {
  return (
    <aside className="app-sidebar">
      <div className="brand">PBA<span>v2</span></div>
      <nav>
        {NAV.map((n) => (
          <a key={n.label} className={'nav-item' + (n.active ? ' active' : '')}>
            <span className="nav-icon">{n.icon}</span>
            <span className="nav-label">{n.label}</span>
          </a>
        ))}
      </nav>
      <div className="sidebar-foot">
        <div className="avatar">JL</div>
        <div className="who">
          <div className="who-name">Jordan Lee</div>
          <div className="who-sub">Solo studio</div>
        </div>
      </div>
    </aside>
  );
}

// ---------- small atoms ----------
function PlatformDot({ p, size = 22 }) {
  const m = PLATFORM_META[p];
  if (!m) return null;
  return (
    <span className="pdot" title={m.label}
      style={{ width: size, height: size, color: m.color, borderColor: m.color + '55' }}>
      {m.code}
    </span>
  );
}

function PlatformRow({ platforms, max = 4 }) {
  const shown = platforms.slice(0, max);
  const extra = platforms.length - shown.length;
  return (
    <div className="prow">
      {shown.map((p) => <PlatformDot key={p} p={p} />)}
      {extra > 0 && <span className="pdot pdot-more">+{extra}</span>}
    </div>
  );
}

function VoiceScore({ score, size = 30 }) {
  if (score == null) {
    return (
      <span className="vscore vscore-empty" title="Not yet voice-checked"
        style={{ width: size, height: size }}>—</span>
    );
  }
  const c = scoreColor(score);
  const deg = (score / 100) * 360;
  return (
    <span className="vscore" title={`Voice match ${score}`}
      style={{ width: size, height: size, background: `conic-gradient(${c} ${deg}deg, #2c2c36 ${deg}deg)` }}>
      <span className="vscore-inner" style={{ color: c }}>{score}</span>
    </span>
  );
}

function StatusTag({ status }) {
  const m = STATUS_META[status];
  return (
    <span className="status-tag" style={{ color: m.color }}>
      <span className="status-dot" style={{ background: m.color }} />
      {m.label}
    </span>
  );
}

// ---------- header ----------
function StudioHeader({ onNew, search, setSearch, view, setView, count }) {
  return (
    <header className="studio-header">
      <div className="sh-top">
        <div className="sh-titles">
          <h1>Content Studio</h1>
          <p>{count} {count === 1 ? 'piece' : 'pieces'} moving through your pipeline</p>
        </div>
        <button className="btn-primary" onClick={onNew}>
          <span className="plus">+</span> New Content
        </button>
      </div>
      <div className="sh-controls">
        <div className="search">
          <span className="search-icon">⌕</span>
          <input value={search} onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by title or tag…" />
        </div>
        <div className="view-toggle">
          <button className={view === 'board' ? 'active' : ''} onClick={() => setView('board')}>
            <span className="vt-glyph">▦</span> Board
          </button>
          <button className={view === 'table' ? 'active' : ''} onClick={() => setView('table')}>
            <span className="vt-glyph">≣</span> Table
          </button>
        </div>
      </div>
    </header>
  );
}

// ---------- pipeline filter bar (replaces checkbox wall) ----------
function PipelineBar({ counts, active, setActive, total }) {
  return (
    <div className="pipeline-bar">
      <button className={'pl-pill pl-all' + (active == null ? ' on' : '')}
        onClick={() => setActive(null)}>
        All <span className="pl-count">{total}</span>
      </button>
      <span className="pl-divider" />
      {STATUSES.map((s) => {
        const m = STATUS_META[s];
        const n = counts[s] || 0;
        return (
          <button key={s}
            className={'pl-pill' + (active === s ? ' on' : '') + (n === 0 ? ' empty' : '')}
            style={active === s ? { borderColor: m.color, color: m.color } : {}}
            onClick={() => setActive(active === s ? null : s)}>
            <span className="pl-dot" style={{ background: m.color }} />
            {m.label} <span className="pl-count">{n}</span>
          </button>
        );
      })}
    </div>
  );
}

Object.assign(window, {
  AppSidebar, PlatformDot, PlatformRow, VoiceScore, StatusTag, StudioHeader, PipelineBar,
});
