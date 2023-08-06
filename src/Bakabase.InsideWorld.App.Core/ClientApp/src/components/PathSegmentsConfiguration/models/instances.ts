import PathSegmentMatcher from '@/components/PathSegmentsConfiguration/models/PathSegmentMatcher';
import { ResourceProperty } from '@/sdk/constants';


const RootPathMatcher = new PathSegmentMatcher({
  property: ResourceProperty.RootPath,
  multiple: false,
  isRequired: true,
  order: 0,
  prerequisites: [],
});

const ResourceMatcher = new PathSegmentMatcher({
  property: ResourceProperty.Resource,
  multiple: false,
  isRequired: true,
  order: 2,
  prerequisites: [],
});

const allMatchers = [
  RootPathMatcher,
  new PathSegmentMatcher({
    property: ResourceProperty.ParentResource,
    multiple: false,
    order: 1,
  }),
  ResourceMatcher,
  new PathSegmentMatcher({
    property: ResourceProperty.ReleaseDt,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Publisher,
    multiple: true,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Name,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Language,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Volume,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Original,
    multiple: true,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Series,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.Tag,
    multiple: true,
    order: 1,
  }),

  // new PathSegmentMatcher({
  //   type: ResourceProperty.Introduction,
  //   multiple: false,
  //   valueType: 'layer',
  // }),

  new PathSegmentMatcher({
    property: ResourceProperty.Rate,
    multiple: false,
    order: 1,
  }),

  new PathSegmentMatcher({
    property: ResourceProperty.CustomProperty,
    multiple: true,
    order: 1,
  }),
];

// Set check order of matchers
const notSetCheckOrderMatchers = allMatchers.slice();
const setCheckOrderMatchers: PathSegmentMatcher[] = [];
while (setCheckOrderMatchers.length < notSetCheckOrderMatchers.length) {
  const doneCount = setCheckOrderMatchers.length;
  for (const m of notSetCheckOrderMatchers) {
    if (!setCheckOrderMatchers.includes(m)) {
      if (m.prerequisites.length == 0) {
        setCheckOrderMatchers.push(m);
      } else {
        if (m.prerequisites.every(p => setCheckOrderMatchers.some(m => m.property == p))) {
          setCheckOrderMatchers.push(m);
        }
      }
    }
  }

  if (doneCount == setCheckOrderMatchers.length) {
    throw new Error('There is a loop in resource prerequisites');
  }
}

for (let i = 0; i < setCheckOrderMatchers.length; i++) {
  setCheckOrderMatchers[i].checkOrder = i;
}

console.log('[AllPathSegmentMatchers]', allMatchers);

const matchersAfter: { [type in ResourceProperty]?: PathSegmentMatcher[] } = allMatchers.reduce((s, t) => {
  s[t.property] = allMatchers.filter(m => m.order > t.order);
  return s;
}, {});

const matchersBefore: { [type in ResourceProperty]?: PathSegmentMatcher[] } = allMatchers.reduce((s, t) => {
  s[t.property] = allMatchers.filter(m => m.order < t.order);
  return s;
}, {});

const buildSimpleMatcherOrders = (data: { [type in ResourceProperty]?: PathSegmentMatcher[] }): {
  [type: string]: string[];
} => Object.keys(data)
  .reduce<{ [type: string]: string[] }>((s, t) => {
    s[ResourceProperty[t]] = data[t]?.map(m => ResourceProperty[m.type]);
    return s;
  }, {});

console.log('[MatchersAfter]', buildSimpleMatcherOrders(matchersAfter));
console.log('[MatchersBefore]', buildSimpleMatcherOrders(matchersBefore));

export {
  PathSegmentMatcher,
  allMatchers,
  matchersAfter,
  matchersBefore,
  RootPathMatcher,
  ResourceMatcher,
};

