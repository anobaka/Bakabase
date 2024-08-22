export type MultilevelData<V> = { value: V; label: string; children?: MultilevelData<V>[] };

export type LinkValue = { text?: string; url?: string };

export type TagValue = { group?: string; name: string };
