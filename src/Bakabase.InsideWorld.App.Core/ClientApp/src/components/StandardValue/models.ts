export type MultilevelData<V> = { value: V; label: string; children?: MultilevelData<V>[] };
