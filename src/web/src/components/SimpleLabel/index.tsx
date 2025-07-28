import "./index.scss";
import React from "react";
interface IProps extends React.HTMLAttributes<HTMLElement> {
  children: any;
  status?: "default" | "primary" | "success" | "warning" | "info" | "danger";
}
const SimpleLabel = (props: IProps) => {
  const { children, status = "default", className, ...otherProps } = props;

  return (
    <span
      className={`simple-label ${status} ${className || ""}`}
      {...otherProps}
    >
      {children}
    </span>
  );
};

SimpleLabel.displayName = "SimpleLabel";

export default SimpleLabel;
