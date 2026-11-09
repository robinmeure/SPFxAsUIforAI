export interface ListSnapshot { generation: number; revision: number; }

/** Reconciles server snapshots without discarding changes made while a list request was in flight. */
export class ChatListState {
  private generation = 0;
  private revision = 0;
  private readonly changes = new Map<string, number>();
  private readonly deleted = new Set<string>();

  public begin(): ListSnapshot { return { generation: ++this.generation, revision: this.revision }; }
  public current(snapshot: ListSnapshot): boolean { return snapshot.generation === this.generation; }
  public invalidate(): void { ++this.generation; }
  public touch(id: string): void { this.changes.set(id, ++this.revision); }
  public remove(id: string): void { this.touch(id); this.deleted.add(id); }
  public promote(from: string, to: string): void { this.remove(from); this.touch(to); }

  public merge<T extends { id: string }>(
    snapshot: ListSnapshot, current: T[], incoming: T[], mergeExisting: (local: T, remote: T) => T
  ): T[] {
    if (!this.current(snapshot)) return current;
    const remoteIds = new Set(incoming.map(chat => chat.id));
    const changed = (id: string): boolean => (this.changes.get(id) ?? 0) > snapshot.revision;
    const retained = current.filter(chat => !this.deleted.has(chat.id) && !remoteIds.has(chat.id)
      && (chat.id.indexOf('local-') === 0 || changed(chat.id)));
    const loaded = incoming.filter(chat => !this.deleted.has(chat.id)).map(remote => {
      const local = current.find(chat => chat.id === remote.id);
      return local ? changed(local.id) ? local : mergeExisting(local, remote) : remote;
    });
    return [...retained, ...loaded];
  }
}
