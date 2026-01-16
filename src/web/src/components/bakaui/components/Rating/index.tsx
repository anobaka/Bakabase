import type { RateProps as RcRateProps, RateRef } from "rc-rate/lib/Rate";

import Rate from "rc-rate/lib/Rate";
import React, { forwardRef } from "react";

import "rc-rate/assets/index.css";

export interface RatingProps extends Omit<RcRateProps, "style"> {
  size?: "sm" | "md" | "lg";
  style?: React.CSSProperties;
}

const sizeMap: Record<"sm" | "md" | "lg", number> = {
  sm: 16,
  md: 20,
  lg: 28,
};

const Rating = forwardRef<RateRef, RatingProps>(
  ({ size = "md", style, ...props }, ref) => {
    const fontSize = sizeMap[size];

    return (
      <Rate
        ref={ref}
        style={{
          fontSize,
          ...style,
        }}
        {...props}
      />
    );
  },
);

export default Rating;
