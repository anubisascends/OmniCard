import { api } from '../api/client';
import type { ScanBatchItemEdit } from '../api/types';

/**
 * Saves a scan batch's per-item edits to the server — the batch counterpart of the IndexedDB
 * `ScanSessionStore`. Each item is compared by the JSON of its editable fields (not by object
 * identity), so items the background matcher updated aren't echoed back to the server: call
 * {@link prime} with anything taken from the server to mark it as already saved.
 *
 * Writes are serialized so a slow PUT can't land after a newer one. A failed write (for example a
 * 409 because the claim was lost) is reported through `onError`.
 */
export class BatchItemStore<T extends { key: string }> {
  private saved = new Map<string, string>();
  private chain: Promise<void> = Promise.resolve();

  constructor(
    private readonly batchId: number,
    private readonly toEdit: (item: T) => ScanBatchItemEdit | null,
    private readonly onError: (error: unknown) => void,
  ) {}

  /** Record items as matching what the server holds (nothing is sent). */
  prime(items: T[]) {
    for (const it of items) {
      const edit = this.toEdit(it);
      if (edit) this.saved.set(it.key, JSON.stringify(edit));
    }
  }

  /** Send every item whose editable fields changed since the last save. */
  sync(items: T[]): Promise<void> {
    const changed: ScanBatchItemEdit[] = [];
    for (const it of items) {
      const edit = this.toEdit(it);
      if (!edit) continue;
      const sig = JSON.stringify(edit);
      if (this.saved.get(it.key) === sig) continue;
      this.saved.set(it.key, sig);
      changed.push(edit);
    }
    if (changed.length > 0) {
      this.chain = this.chain
        .then(() => api.scanBatchSaveItems(this.batchId, changed))
        .catch((e) => this.onError(e));
    }
    return this.chain;
  }
}
