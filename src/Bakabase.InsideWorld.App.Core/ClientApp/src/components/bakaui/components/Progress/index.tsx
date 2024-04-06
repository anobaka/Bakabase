import { Progress } from 'antd';

interface ProgressProps {
  className?: string;
  percent: number;
  renderPercent?: (percent: number) => any;
}

export default ({ renderPercent, ...otherProps }: ProgressProps) => {
  return (
    <Progress
      format={renderPercent}
      {...otherProps}
    />
  );
};
