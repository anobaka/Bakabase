// import type { Dayjs } from 'dayjs';
// import dayjs from 'dayjs';
// import type { Duration } from 'dayjs/plugin/duration';
// import StringValueRenderer from './Renderers/StringValueRenderer';
// import ListStringValueRenderer from './Renderers/ListStringValueRenderer';
// import NumberValueRenderer from './Renderers/NumberValueRenderer';
// import BooleanValueRenderer from './Renderers/BooleanValueRenderer';
// import LinkValueRenderer from './Renderers/LinkValueRenderer';
// import DateTimeValueRenderer from './Renderers/DateTimeValueRenderer';
// import { StandardValueType } from '@/sdk/constants';
// import type { LinkValue } from '@/components/StandardValue/models';
//
// type Props = {
//   value?: any;
//   type: StandardValueType;
//   onClick?: () => any;
// };
//
// export default ({ value, type, onClick }: Props) => {
//   switch (type) {
//     case StandardValueType.String:
//       return (
//         <StringValueRenderer
//           value={value as string}
//           onClick={onClick}
//         />
//       );
//     case StandardValueType.ListString:
//       return (
//         <ListStringValueRenderer
//           value={value as string[]}
//           onClick={onClick}
//         />
//       );
//     case StandardValueType.Decimal:
//       return (
//         <NumberValueRenderer
//           value={value as number}
//           onClick={onClick}
//         />
//       );
//     case StandardValueType.Link:
//       return (
//         <LinkValueRenderer
//           value={value as LinkValue}
//           onClick={onClick}
//         />
//       );
//     case StandardValueType.Boolean:
//       return (
//         <BooleanValueRenderer
//           value={value as boolean}
//           onClick={onClick}
//         />
//       );
//     case StandardValueType.DateTime: {
//       const stringDateTime = value as string;
//       let date: Dayjs | undefined;
//       if (stringDateTime != undefined && stringDateTime.length > 0) {
//         date = dayjs(stringDateTime);
//       }
//       return (
//         <DateTimeValueRenderer
//           value={date}
//           format={'YYYY-MM-DD HH:mm:ss'}
//           onClick={onClick}
//         />
//       );
//     }
//     case StandardValueType.Time: {
//       const stringTime = value as string;
//       let time: Duration | undefined;
//       if (stringTime != undefined && stringTime.length > 0) {
//         time = dayjs.duration(stringTime);
//       }
//       return (
//         <DateTimeValueRenderer
//           value={time}
//           format={'HH:mm:ss'}
//           onClick={onClick}
//         />
//       );
//     }
//     case StandardValueType.ListListString: {
//       const data = value as string[][] ?? [];
//       const str = data.
//       return(
//         <StringValueRenderer
//           value={value as string}
//           onClick={onClick}
//         />,
//       );
//     }
//     case StandardValueType.ListTag:
//       return (
//         <StringValueRenderer
//           value={value as string}
//           onClick={onClick}
//         />
//       );
//   }
// };
