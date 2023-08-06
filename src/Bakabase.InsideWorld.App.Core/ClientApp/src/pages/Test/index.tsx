import React, { useCallback, useRef, useState } from 'react';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import { ResourceProperty } from '@/sdk/constants';
import './index.scss';

import type { RJSFSchema, UiSchema, FieldProps, RegistryFieldsType } from '@rjsf/utils';
import validator from '@rjsf/validator-ajv8';
import Form from '@rjsf/core';

const schema: RJSFSchema = {
  type: 'object',
  required: ['lat', 'lon'],
  properties: {
    lat: { type: 'number' },
    lon: { type: 'number' },
  },
};

// Define a custom component for handling the root position object
class GeoPosition extends React.Component<FieldProps> {
  constructor(props: FieldProps) {
    super(props);
    this.state = { ...props.formData };
  }

  onChange(name) {
    return (event) => {
      this.setState(
        {
          [name]: parseFloat(event.target.value),
        },
        () => {
          console.log(this.state);
          this.props.onChange(this.state);
        },
      );
    };
  }

  render() {
    const { lat, lon } = this.state;
    return (
      <div>
        <input type="number" value={lat} onChange={this.onChange('lat')} />
        <input type="number" value={lon} onChange={this.onChange('lon')} />
      </div>
    );
  }
}

// Define the custom field component to use for the root object
const uiSchema: UiSchema = { 'ui:field': 'geo' };

// Define the custom field components to register; here our "geo"
// custom field component
const fields: RegistryFieldsType = { geo: GeoPosition };

// Render the form with all the properties we just defined passed
// as props
export default () => {
  return <Form schema={schema} uiSchema={uiSchema} validator={validator} fields={fields} />;
};

// export default () => {
//   const [samplePath, setSamplePath] = useState('D:\\test\\new-media-library-path-configuration\\a\\bc\\New Text Document.txt');
//   const [value, setValue] = useState({});
//   const segmentsRef = useRef(samplePath.split('\\'));
//
//   const simpleMatchers = {
//     [ResourceProperty.Resource]: false,
//     [ResourceProperty.RootPath]: false,
//     [ResourceProperty.ParentResource]: false,
//     [ResourceProperty.ReleaseDt]: false,
//     [ResourceProperty.Publisher]: false,
//     [ResourceProperty.Name]: false,
//     [ResourceProperty.Language]: false,
//     [ResourceProperty.Volume]: false,
//     [ResourceProperty.Original]: false,
//     [ResourceProperty.Series]: false,
//     [ResourceProperty.Tag]: false,
//     // [MatcherType.Introduction]: false,
//     [ResourceProperty.Rate]: false,
//     [ResourceProperty.CustomProperty]: false,
//   };
//   const matchers = Object.keys(simpleMatchers)
//     .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
//       ts.push(new PathSegmentConfigurationPropsMatcherOptions({
//         type: parseInt(t),
//         readonly: simpleMatchers[t],
//       }));
//       return ts;
//     }, []);
//
//   const matchersRef = useRef(matchers);
//   const onChangeCallback = useCallback(v => {
//     setValue(v);
//   }, []);
//   const valueRef = useRef({ 1: ['D:/test'], 2: [{ isReverse: true, layer: 3 }], 3: ['^[^\\/]+\\/[^\\/]+\\/[^\\/]+\\/[^\\/]+$'] });
//
//   return (
//     <div className={'test-page'}>
//       <div className="psc">
//         <PathSegmentsConfiguration
//           isDirectory={false}
//           segments={segmentsRef.current}
//           matchers={matchersRef.current}
//           defaultValue={valueRef.current}
//           onChange={onChangeCallback}
//         />
//         <div className="values">
//           <div className="matcher">
//             <div className="label">Raw</div>
//             <div className="value">
//               {JSON.stringify(value)}
//             </div>
//           </div>
//           {Object.keys(value).map(m => {
//             const t = parseInt(m);
//             let v;
//             if (t == ResourceProperty.RootPath || t == ResourceProperty.Resource) {
//               v = value[t][0];
//             } else {
//               v = JSON.stringify(value[m]);
//             }
//             return (
//               <div className={'matcher'}>
//                 <div className="label">{ResourceProperty[m]}</div>
//                 <div className="value">{v}</div>
//               </div>
//             );
//           })}
//         </div>
//       </div>
//     </div>
//   );
// };
