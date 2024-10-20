import './index.scss';

interface Props {
  color?: string;
  direction?: 'left' | 'right' | 'up' | 'down';
  className?: string;
}
export default ({ color = '#999', direction = 'right', className }: Props) => {
  const spans: any[] = [];

  let rotate = -90;
  switch (direction) {
    case 'left':
      rotate = 90;
      break;
    case 'right':
      break;
    case 'up':
      rotate = 180;
      break;
    case 'down':
      rotate = 0;
      break;
  }

  for (let i = 0; i < 3; i++) {
    spans.push(
      <span style={{ borderColor: color }} key={i} />,
    );
  }

  return (
    <div className={`animated-arrow ${className}`}>
      <div className="arrow" style={{ transform: `rotate(${rotate}deg)` }}>
        {spans}
      </div>
    </div>
  );
};
