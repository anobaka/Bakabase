export interface CssVariableDefinition {
  /** CSS variable name */
  name: string;
  /** i18n key for the label */
  labelKey: string;
  /** Default value (with unit) */
  defaultValue: string;
  /** Unit */
  unit: string;
  /** Slider min value */
  min: number;
  /** Slider max value */
  max: number;
  /** Slider step */
  step: number;
}

export interface CssVariableGroup {
  /** i18n key for the group label */
  labelKey: string;
  /** Variables in this group */
  variables: CssVariableDefinition[];
}

export const cssVariableGroups: CssVariableGroup[] = [
  {
    labelKey: "resource.display.styleGroup.resourceList",
    variables: [
      {
        name: "--resource-cover-property-font-size",
        labelKey: "resource.display.coverPropertyFontSize",
        defaultValue: "12px",
        unit: "px",
        min: 8,
        max: 20,
        step: 1,
      },
    ],
  },
];

/** Flat list of all CSS variable definitions (for CSS injection) */
export const allCssVariableDefinitions: CssVariableDefinition[] =
  cssVariableGroups.flatMap((g) => g.variables);
