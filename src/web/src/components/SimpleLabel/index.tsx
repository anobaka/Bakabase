import "./index.scss";
import * as React from "react";
interface IProps extends React.HTMLAttributes<HTMLElement> {
  children: any;
  status?: "default" | "primary" | "success" | "warning" | "info" | "danger";
}

export default (props: IProps) => {
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
