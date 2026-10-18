import type { HTMLAttributes, ReactNode } from "react";

import { forwardRef } from "react";

type Props = HTMLAttributes<HTMLDivElement> & {
  icon: ReactNode;
  trailing?: ReactNode;
};

const ContextMenuRow = forwardRef<HTMLDivElement, Props>(
  ({ icon, children, trailing, className, ...props }, ref) => (
    <div
      ref={ref}
      className={`resource-context-menu__row${className ? ` ${className}` : ""}`}
      {...props}
    >
      <span aria-hidden className="resource-context-menu__icon">
        {icon}
      </span>
      <span className="resource-context-menu__label">{children}</span>
      {trailing}
    </div>
  ),
);

ContextMenuRow.displayName = "ContextMenuRow";

export default ContextMenuRow;
