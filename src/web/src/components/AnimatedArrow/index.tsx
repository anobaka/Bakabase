import "./index.scss";

interface Props {
  color?: string;
  direction?: "left" | "right" | "up" | "down";
  className?: string;
}
const AnimatedArrow = ({
  color = "#999",
  direction = "right",
  className,
}: Props) => {
  const spans: any[] = [];

  let rotate = -90;

  switch (direction) {
    case "left":
      rotate = 90;
      break;
    case "right":
      break;
    case "up":
      rotate = 180;
      break;
    case "down":
      rotate = 0;
      break;
  }

  for (let i = 0; i < 3; i++) {
    spans.push(<span key={i} style={{ borderColor: color }} />);
  }

  return (
    <div className={`animated-arrow ${className}`}>
      <div className="arrow" style={{ transform: `rotate(${rotate}deg)` }}>
        {spans}
      </div>
    </div>
  );
};

AnimatedArrow.displayName = "AnimatedArrow";
export default AnimatedArrow;
