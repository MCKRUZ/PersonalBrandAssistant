// ===== PBAv2 Content Studio — views (board, table, empty, drawer) =====
const { useState: useStateV, useMemo, useRef: useRefV } = React;

// ---------- content card ----------
function ContentCard({ c, onOpen, onDragStart, dragging }) {
  const t = TYPE_META[c.type];
  return (
    <article
      className={'card' + (dragging ? ' dragging' : '')}
      draggable
      onDragStart={(e) => onDragStart(e, c)}
      onClick={() => onOpen(c)}>
      <div className="card-top">
        <span className="type-glyph" title={t.label}>{t.glyph}</span>
        <span className="type-label">{t.label}</span>
        <span className="card-spacer" />
        <VoiceScore score={c.voiceScore} size={26} />
      </div>
      <h3 className="card-title">{c.title}</h3>
      {c.tags.length > 0 && (
        <div className="tagrow">
          {c.tags.map((tg) => <span key={tg} className="tag">#{tg}</span>)}
        </div>
      )}
      <div className="card-foot">
        <PlatformRow platforms={c.targetPlatforms} />
        <span className="updated">
          {c.scheduledAt ? '◴ ' + relTime(c.scheduledAt) : relTime(c.updatedAt)}
        </span>
      </div>
    </article>
  );
}

// ---------- kanban board ----------
function Board({ contents, setContents, onOpen, showArchived }) {
  const [dragId, setDragId] = useStateV(null);
  const [overCol, setOverCol] = useStateV(null);

  const cols = STATUSES.filter((s) => showArchived || s !== 'Archived');
  const byStatus = useMemo(() => {
    const m = {};
    cols.forEach((s) => (m[s] = []));
    contents.forEach((c) => { if (m[c.status]) m[c.status].push(c); });
    return m;
  }, [contents, showArchived]);

  const onDragStart = (e, c) => {
    setDragId(c.id);
    e.dataTransfer.effectAllowed = 'move';
    try { e.dataTransfer.setData('text/plain', c.id); } catch (_) {}
  };
  const onDrop = (status) => {
    if (!dragId) return;
    setContents((prev) => prev.map((c) =>
      c.id === dragId ? { ...c, status, updatedAt: new Date().toISOString() } : c));
    setDragId(null);
    setOverCol(null);
  };

  return (
    <div className="board">
      {cols.map((s) => {
        const m = STATUS_META[s];
        const items = byStatus[s];
        return (
          <section
            key={s}
            className={'col' + (overCol === s ? ' col-over' : '')}
            onDragOver={(e) => { e.preventDefault(); setOverCol(s); }}
            onDragLeave={() => setOverCol((c) => (c === s ? null : c))}
            onDrop={() => onDrop(s)}>
            <header className="col-head">
              <span className="col-dot" style={{ background: m.color }} />
              <span className="col-name">{m.label}</span>
              <span className="col-count">{items.length}</span>
            </header>
            <div className="col-body">
              {items.map((c) => (
                <ContentCard key={c.id} c={c} onOpen={onOpen}
                  onDragStart={onDragStart} dragging={dragId === c.id} />
              ))}
              {items.length === 0 && (
                <div className="col-empty">Drop here</div>
              )}
            </div>
          </section>
        );
      })}
    </div>
  );
}

// ---------- table view ----------
function Table({ contents, onOpen }) {
  if (contents.length === 0) return null;
  return (
    <div className="table-wrap">
      <table className="ctable">
        <thead>
          <tr>
            <th>Status</th><th>Title</th><th>Type</th>
            <th>Platforms</th><th>Voice</th><th className="ta-r">Updated</th>
          </tr>
        </thead>
        <tbody>
          {contents.map((c) => (
            <tr key={c.id} onClick={() => onOpen(c)}>
              <td><StatusTag status={c.status} /></td>
              <td className="td-title">{c.title}
                {c.tags.length > 0 && <span className="td-tags">{c.tags.map((t) => '#' + t).join(' ')}</span>}
              </td>
              <td className="td-type"><span className="type-glyph sm">{TYPE_META[c.type].glyph}</span>{TYPE_META[c.type].label}</td>
              <td><PlatformRow platforms={c.targetPlatforms} max={5} /></td>
              <td><VoiceScore score={c.voiceScore} size={26} /></td>
              <td className="ta-r td-updated">{c.scheduledAt ? '◴ ' + relTime(c.scheduledAt) : relTime(c.updatedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ---------- inspiring empty state ----------
function EmptyState({ onNew, filtered, onClearFilter }) {
  if (filtered) {
    return (
      <div className="empty">
        <div className="empty-mark">⌕</div>
        <h2 className="empty-h">Nothing matches that filter</h2>
        <p className="empty-p">Try a different stage or clear your search to see everything in the pipeline.</p>
        <button className="btn-ghost" onClick={onClearFilter}>Clear filters</button>
      </div>
    );
  }
  return (
    <div className="empty">
      <div className="empty-mark">✎</div>
      <h2 className="empty-h">Your studio is quiet.</h2>
      <p className="empty-p">Every great post starts as a rough idea. Here are a few angles
        pulled from what's resonating in your space — start a draft in one click.</p>
      <div className="idea-grid">
        {IDEA_SUGGESTIONS.map((s, i) => (
          <button key={i} className="idea-card" onClick={onNew}>
            <span className="idea-topic">{s.topic}</span>
            <span className="idea-hook">{s.hook}</span>
            <span className="idea-cta">Start draft <span className="type-glyph sm">{TYPE_META[s.type].glyph}</span></span>
          </button>
        ))}
      </div>
      <div className="empty-or"><span>or</span></div>
      <button className="btn-primary lg" onClick={onNew}><span className="plus">+</span> Start from scratch</button>
    </div>
  );
}

// ---------- detail sidecar drawer ----------
function DetailDrawer({ c, onClose, onAdvance, onOpenEditor, onPublish }) {
  if (!c) return null;
  const idx = STATUSES.indexOf(c.status);
  const next = STATUSES[idx + 1];
  const canPublish = c.status === 'Approved' || c.status === 'Scheduled';
  return (
    <>
      <div className="scrim" onClick={onClose} />
      <aside className="drawer">
        <header className="drawer-head">
          <StatusTag status={c.status} />
          <button className="x" onClick={onClose}>✕</button>
        </header>
        <div className="drawer-body">
          <span className="type-label big">{TYPE_META[c.type].glyph}&nbsp; {TYPE_META[c.type].label}</span>
          <h2 className="drawer-title">{c.title}</h2>
          <div className="drawer-meta">
            <div className="dm-row">
              <span className="dm-k">Voice match</span>
              <span className="dm-v"><VoiceScore score={c.voiceScore} size={34} /></span>
            </div>
            <div className="dm-row">
              <span className="dm-k">Platforms</span>
              <span className="dm-v"><PlatformRow platforms={c.targetPlatforms} max={6} /></span>
            </div>
            <div className="dm-row">
              <span className="dm-k">{c.scheduledAt ? 'Scheduled' : 'Updated'}</span>
              <span className="dm-v dim">{relTime(c.scheduledAt || c.updatedAt)}</span>
            </div>
            {c.tags.length > 0 && (
              <div className="dm-row">
                <span className="dm-k">Tags</span>
                <span className="dm-v"><span className="tagrow">{c.tags.map((t) => <span key={t} className="tag">#{t}</span>)}</span></span>
              </div>
            )}
          </div>

          {(c.status === 'Approved' || c.status === 'Scheduled' || c.status === 'Published') && (
            <div className="dd">
              <div className="dd-title">{c.status === 'Scheduled' ? 'Scheduled delivery' : c.status === 'Published' ? 'Published to' : 'How this publishes'}</div>
              {c.status === 'Scheduled' && (
                <p className="dd-note">Goes out <b>{relTime(c.scheduledAt)}</b>. Auto destinations publish themselves; manual ones become a to-do for you.</p>
              )}
              {c.status === 'Approved' && (
                <p className="dd-note">Hit <b>Publish</b> to deploy. Auto destinations go live instantly; you post manual ones yourself.</p>
              )}
              <div className="dd-list">
                {c.targetPlatforms.filter((p) => PUBLISHABLE.includes(p)).map((p) => {
                  const m = PLATFORM_META[p];
                  const auto = m.delivery === 'auto' && m.connected;
                  const needs = m.delivery === 'auto' && !m.connected;
                  return (
                    <div key={p} className="dd-row">
                      <span className="dest-dot" style={{ color: m.color, borderColor: m.color + '66' }}>{m.code}</span>
                      <span className="dd-name">{m.label}</span>
                      {c.status === 'Published'
                        ? <span className="dd-badge a">✓ {auto ? 'Published' : 'Posted'}</span>
                        : <span className={'dd-badge ' + (auto ? 'a' : needs ? 'w' : 'm')}>{auto ? '⚡ Auto' : needs ? '⚡ Connect' : '✋ Manual'}</span>}
                    </div>
                  );
                })}
              </div>
            </div>
          )}

          <div className="drawer-preview">
            <div className="dp-line" style={{ width: '92%' }} />
            <div className="dp-line" style={{ width: '100%' }} />
            <div className="dp-line" style={{ width: '78%' }} />
            <div className="dp-line dp-gap" style={{ width: '88%' }} />
            <div className="dp-line" style={{ width: '64%' }} />
            <p className="dp-note">// body preview — open in editor to write</p>
          </div>
        </div>
        <footer className="drawer-foot">
          <button className="btn-ghost" onClick={() => onOpenEditor && onOpenEditor(c)}>Open in editor</button>
          {canPublish ? (
            <button className="btn-primary" onClick={() => onPublish && onPublish(c, 'publish')}>
              {c.status === 'Scheduled' ? 'Publish now →' : 'Publish →'}
            </button>
          ) : next && next !== 'Archived' && (
            <button className="btn-primary" onClick={() => onAdvance(c, next)}>
              Move to {next} →
            </button>
          )}
        </footer>
      </aside>
    </>
  );
}

Object.assign(window, { ContentCard, Board, Table, EmptyState, DetailDrawer });
