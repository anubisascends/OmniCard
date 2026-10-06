/**
 * Keeps an in-progress scan list in IndexedDB so a page refresh or crash (mid-recording, say) doesn't
 * lose it. Scans otherwise live only in the page's memory until they're committed.
 *
 * Layout: `files` holds each scan's image once (written when the item first appears — images are
 * large, and an item is rewritten many times as it matches / is edited); `items` holds everything
 * else; `sessions` holds the list order + game. Each scan surface keys its own session ("scan", or
 * "audit-<locationId>"). Everything is best-effort: if IndexedDB is unavailable (private window,
 * blocked storage, quota) the page simply works without persistence.
 */

const DB_NAME = 'omnicard-scan-sessions';
const DB_VERSION = 1;

/** What the store needs of a scan item; every other field must be structured-cloneable. A blob
 * preview URL is stored too but is dead in a new tab — the caller recreates it from `file`. */
export interface StoredScanItem {
  key: string;
  /** Items without a local file (e.g. background-batch items) aren't persisted here. */
  file?: File;
}

export interface RestoredSession<T> {
  game: string;
  items: T[];
}

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = () => {
      const db = req.result;
      db.createObjectStore('sessions', { keyPath: 'session' });
      db.createObjectStore('items', { keyPath: ['session', 'key'] }).createIndex('session', 'session');
      db.createObjectStore('files', { keyPath: ['session', 'key'] }).createIndex('session', 'session');
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function done(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

function getAll<T>(store: IDBObjectStore | IDBIndex, query: IDBValidKey | IDBKeyRange): Promise<T[]> {
  return new Promise((resolve, reject) => {
    const req = store.getAll(query);
    req.onsuccess = () => resolve(req.result as T[]);
    req.onerror = () => reject(req.error);
  });
}

function get<T>(store: IDBObjectStore, key: IDBValidKey): Promise<T | undefined> {
  return new Promise((resolve, reject) => {
    const req = store.get(key);
    req.onsuccess = () => resolve(req.result as T | undefined);
    req.onerror = () => reject(req.error);
  });
}

/**
 * A persisted scan session. Call {@link load} once on mount, then {@link sync} with the current list
 * whenever it changes (diffed by object identity, so only changed items are rewritten).
 */
export class ScanSessionStore<T extends StoredScanItem> {
  private dbPromise: Promise<IDBDatabase> | null = null;
  private saved = new Map<string, T>();
  private chain: Promise<void> = Promise.resolve();

  constructor(private readonly session: string) {}

  private db(): Promise<IDBDatabase> {
    this.dbPromise ??= openDb();
    return this.dbPromise;
  }

  async load(): Promise<RestoredSession<T> | null> {
    try {
      const db = await this.db();
      const tx = db.transaction(['sessions', 'items', 'files'], 'readonly');
      // Issue every request up front: a transaction auto-commits once it has nothing pending.
      const [meta, rows, files] = await Promise.all([
        get<{ game: string; order: string[] }>(tx.objectStore('sessions'), this.session),
        getAll<{ key: string; data: Omit<T, 'file'> }>(tx.objectStore('items').index('session'), this.session),
        getAll<{ key: string; file: File }>(tx.objectStore('files').index('session'), this.session),
      ]);
      if (!meta) return null;
      const fileByKey = new Map(files.map((f) => [f.key, f.file]));
      const byKey = new Map<string, T>();
      for (const row of rows) {
        const file = fileByKey.get(row.key);
        if (file) byKey.set(row.key, { ...row.data, key: row.key, file } as T);
      }
      const items = meta.order.map((k) => byKey.get(k)).filter((it): it is T => !!it);
      // The restored list is exactly what's stored, so the next sync diffs against it.
      this.saved = new Map(items.map((it) => [it.key, it]));
      return { game: meta.game, items };
    } catch (e) {
      console.warn('Scan session restore failed', e);
      return null;
    }
  }

  /** Persist the list. Writes are serialized so a slow write can't land after a newer one. */
  sync(game: string, items: T[]): Promise<void> {
    const prev = this.saved;
    const next = new Map(items.map((it) => [it.key, it]));
    this.saved = next;
    this.chain = this.chain.then(() => this.write(game, items, prev)).catch((e) => {
      console.warn('Scan session save failed', e);
    });
    return this.chain;
  }

  private async write(game: string, items: T[], prev: Map<string, T>): Promise<void> {
    const db = await this.db();
    const tx = db.transaction(['sessions', 'items', 'files'], 'readwrite');
    const itemStore = tx.objectStore('items');
    const fileStore = tx.objectStore('files');
    const keys = new Set<string>();
    for (const it of items) {
      if (!it.file) continue;
      keys.add(it.key);
      const before = prev.get(it.key);
      if (before === it) continue;
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      const { file, ...data } = it;
      itemStore.put({ session: this.session, key: it.key, data });
      if (!before) fileStore.put({ session: this.session, key: it.key, file });
    }
    for (const key of prev.keys()) {
      if (keys.has(key)) continue;
      itemStore.delete([this.session, key]);
      fileStore.delete([this.session, key]);
    }
    const sessions = tx.objectStore('sessions');
    if (items.length === 0) sessions.delete(this.session);
    else sessions.put({ session: this.session, game, order: items.map((it) => it.key) });
    await done(tx);
  }
}
