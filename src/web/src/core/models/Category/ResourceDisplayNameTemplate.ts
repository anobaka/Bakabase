export type ResourceDisplayNameTemplateSegment = { text: string; type: ResourceDisplayNameTemplateSegmentType };

export enum ResourceDisplayNameTemplateSegmentType {
  Text = 1,
  Wrapper = 2,
  Property = 3,
}

