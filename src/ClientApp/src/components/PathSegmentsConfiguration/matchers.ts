import PscMatcher from './models/PscMatcher';
import { PscPropertyType } from './models/PscPropertyType';


const RootPathMatcher = new PscMatcher({
  propertyType: PscPropertyType.RootPath,
  multiple: false,
  isRequired: true,
  order: 0,
  prerequisites: [],
});

const ResourceMatcher = new PscMatcher({
  propertyType: PscPropertyType.Resource,
  multiple: false,
  isRequired: true,
  order: 2,
  prerequisites: [],
});

const allMatchers = [
  RootPathMatcher,
  new PscMatcher({
    propertyType: PscPropertyType.ParentResource,
    multiple: false,
    order: 1,
  }),
  ResourceMatcher,
  new PscMatcher({
    propertyType: PscPropertyType.Rating,
    multiple: false,
    order: 1,
  }),
  new PscMatcher({
    propertyType: PscPropertyType.Introduction,
    multiple: false,
    order: 1,
  }),
  new PscMatcher({
    propertyType: PscPropertyType.CustomProperty,
    multiple: true,
    order: 1,
  }),
];

// Set check order of matchers
const notSetCheckOrderMatchers = allMatchers.slice();
const setCheckOrderMatchers: PscMatcher[] = [];
while (setCheckOrderMatchers.length < notSetCheckOrderMatchers.length) {
  const doneCount = setCheckOrderMatchers.length;
  for (const m of notSetCheckOrderMatchers) {
    if (!setCheckOrderMatchers.includes(m)) {
      if (m.prerequisites.length == 0) {
        setCheckOrderMatchers.push(m);
      } else {
        if (m.prerequisites.every(p => setCheckOrderMatchers.some(m => m.propertyType == p))) {
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

const matchersAfter: { [type in PscPropertyType]?: PscMatcher[] } = allMatchers.reduce((s, t) => {
  s[t.propertyType] = allMatchers.filter(m => m.order > t.order);
  return s;
}, {});

const matchersBefore: { [type in PscPropertyType]?: PscMatcher[] } = allMatchers.reduce((s, t) => {
  s[t.propertyType] = allMatchers.filter(m => m.order < t.order);
  return s;
}, {});

const buildSimpleMatcherOrders = (data: { [type in PscPropertyType]?: PscMatcher[] }): {
  [type: string]: string[];
} => Object.keys(data)
  .reduce<{ [type: string]: string[] }>((s, t) => {
    s[PscPropertyType[t]] = data[t]?.map(m => PscPropertyType[m.type]);
    return s;
  }, {});

console.log('[MatchersAfter]', buildSimpleMatcherOrders(matchersAfter));
console.log('[MatchersBefore]', buildSimpleMatcherOrders(matchersBefore));

export {
  allMatchers,
  matchersAfter,
  matchersBefore,
  RootPathMatcher,
  ResourceMatcher,
};

