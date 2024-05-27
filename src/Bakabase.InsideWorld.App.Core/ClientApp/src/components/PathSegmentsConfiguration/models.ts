import type PscProperty from './models/PscProperty';

export type OnDeleteMatcherValue = (property: PscProperty, index?: number) => void;
