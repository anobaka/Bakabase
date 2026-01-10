"use client";

import type { SearchFilterGroup } from "@/components/ResourceFilter";

import React, { useState, useRef, useEffect } from "react";
import ReactJson from "react-json-view";
import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";

import {
  ResourceFilterController,
  GroupCombinator,
} from "@/components/ResourceFilter";
import { FilterDisplayMode, PropertyPool, PropertyType, SearchOperation, StandardValueType } from "@/sdk/constants";
import { Card, CardBody, CardHeader, Tabs, Tab } from "@/components/bakaui";
import { serializeStandardValue } from "@/components/StandardValue/helpers";

dayjs.extend(duration);

// ============================================
// Mock Properties for All Property Types
// ============================================

// 1. SingleLineText
const mockSingleLineTextProperty = {
  id: 1,
  pool: PropertyPool.Custom,
  name: "Title",
  type: PropertyType.SingleLineText,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleLineText",
  poolName: "Custom",
  order: 0,
};

// 2. MultilineText
const mockMultilineTextProperty = {
  id: 2,
  pool: PropertyPool.Custom,
  name: "Description",
  type: PropertyType.MultilineText,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "MultilineText",
  poolName: "Custom",
  order: 1,
};

// 3. SingleChoice
const mockSingleChoiceProperty = {
  id: 3,
  pool: PropertyPool.Custom,
  name: "Status",
  type: PropertyType.SingleChoice,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleChoice",
  poolName: "Custom",
  order: 2,
  options: {
    choices: [
      { value: "draft", label: "Draft", color: "#888888" },
      { value: "published", label: "Published", color: "#22c55e" },
      { value: "archived", label: "Archived", color: "#f59e0b" },
    ],
  },
};

// 3b. SingleChoice without options
const mockSingleChoiceNoOptionsProperty = {
  id: 31,
  pool: PropertyPool.Custom,
  name: "Status (No Options)",
  type: PropertyType.SingleChoice,
  dbValueType: StandardValueType.String,
  bizValueType: StandardValueType.String,
  typeName: "SingleChoice",
  poolName: "Custom",
  order: 2,
};

// 4. MultipleChoice
const mockMultipleChoiceProperty = {
  id: 4,
  pool: PropertyPool.Custom,
  name: "Categories",
  type: PropertyType.MultipleChoice,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListString,
  typeName: "MultipleChoice",
  poolName: "Custom",
  order: 3,
  options: {
    choices: [
      { value: "action", label: "Action", color: "#ef4444" },
      { value: "comedy", label: "Comedy", color: "#f59e0b" },
      { value: "drama", label: "Drama", color: "#3b82f6" },
      { value: "sci-fi", label: "Sci-Fi", color: "#8b5cf6" },
    ],
  },
};

// 4b. MultipleChoice without options
const mockMultipleChoiceNoOptionsProperty = {
  id: 41,
  pool: PropertyPool.Custom,
  name: "Categories (No Options)",
  type: PropertyType.MultipleChoice,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListString,
  typeName: "MultipleChoice",
  poolName: "Custom",
  order: 3,
};

// 5. Number
const mockNumberProperty = {
  id: 5,
  pool: PropertyPool.Custom,
  name: "Count",
  type: PropertyType.Number,
  dbValueType: StandardValueType.Decimal,
  bizValueType: StandardValueType.Decimal,
  typeName: "Number",
  poolName: "Custom",
  order: 4,
};

// 6. Percentage
const mockPercentageProperty = {
  id: 6,
  pool: PropertyPool.Custom,
  name: "Progress",
  type: PropertyType.Percentage,
  dbValueType: StandardValueType.Decimal,
  bizValueType: StandardValueType.Decimal,
  typeName: "Percentage",
  poolName: "Custom",
  order: 5,
};

// 7. Rating
const mockRatingProperty = {
  id: 7,
  pool: PropertyPool.Custom,
  name: "Rating",
  type: PropertyType.Rating,
  dbValueType: StandardValueType.Decimal,
  bizValueType: StandardValueType.Decimal,
  typeName: "Rating",
  poolName: "Custom",
  order: 6,
};

// Rating value property (uses Number type for value input)
const mockRatingValueProperty = {
  id: 7,
  pool: PropertyPool.Custom,
  name: "Rating",
  type: PropertyType.Number,
  dbValueType: StandardValueType.Decimal,
  bizValueType: StandardValueType.Decimal,
  typeName: "Number",
  poolName: "Custom",
  order: 6,
};

// 8. Boolean
const mockBooleanProperty = {
  id: 8,
  pool: PropertyPool.Custom,
  name: "Active",
  type: PropertyType.Boolean,
  dbValueType: StandardValueType.Boolean,
  bizValueType: StandardValueType.Boolean,
  typeName: "Boolean",
  poolName: "Custom",
  order: 7,
};

// 9. Date
const mockDateProperty = {
  id: 9,
  pool: PropertyPool.Custom,
  name: "Release Date",
  type: PropertyType.Date,
  dbValueType: StandardValueType.DateTime,
  bizValueType: StandardValueType.DateTime,
  typeName: "Date",
  poolName: "Custom",
  order: 8,
};

// 10. DateTime
const mockDateTimeProperty = {
  id: 10,
  pool: PropertyPool.Custom,
  name: "Created At",
  type: PropertyType.DateTime,
  dbValueType: StandardValueType.DateTime,
  bizValueType: StandardValueType.DateTime,
  typeName: "DateTime",
  poolName: "Custom",
  order: 9,
};

// 11. Time
const mockTimeProperty = {
  id: 11,
  pool: PropertyPool.Custom,
  name: "Duration",
  type: PropertyType.Time,
  dbValueType: StandardValueType.Time,
  bizValueType: StandardValueType.Time,
  typeName: "Time",
  poolName: "Custom",
  order: 10,
};

// 12. Tags
const mockTagsProperty = {
  id: 12,
  pool: PropertyPool.Custom,
  name: "Tags",
  type: PropertyType.Tags,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListListString,
  typeName: "Tags",
  poolName: "Custom",
  order: 11,
  options: {
    tags: [
      { value: "tag1", name: "Important", group: "Priority", color: "#ef4444" },
      { value: "tag2", name: "Urgent", group: "Priority", color: "#f59e0b" },
      { value: "tag3", name: "Work", group: "Category", color: "#3b82f6" },
      { value: "tag4", name: "Personal", group: "Category", color: "#22c55e" },
    ],
  },
};

// 12b. Tags without options
const mockTagsNoOptionsProperty = {
  id: 121,
  pool: PropertyPool.Custom,
  name: "Tags (No Options)",
  type: PropertyType.Tags,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListListString,
  typeName: "Tags",
  poolName: "Custom",
  order: 11,
};

// 13. Multilevel
const mockMultilevelProperty = {
  id: 13,
  pool: PropertyPool.Custom,
  name: "Location",
  type: PropertyType.Multilevel,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListListString,
  typeName: "Multilevel",
  poolName: "Custom",
  order: 12,
  options: {
    data: [
      {
        value: "asia",
        label: "Asia",
        children: [
          { value: "china", label: "China", children: [{ value: "beijing", label: "Beijing" }, { value: "shanghai", label: "Shanghai" }] },
          { value: "japan", label: "Japan", children: [{ value: "tokyo", label: "Tokyo" }] },
        ],
      },
      {
        value: "europe",
        label: "Europe",
        children: [
          { value: "uk", label: "UK", children: [{ value: "london", label: "London" }] },
        ],
      },
    ],
  },
};

// 13b. Multilevel without options
const mockMultilevelNoOptionsProperty = {
  id: 131,
  pool: PropertyPool.Custom,
  name: "Location (No Options)",
  type: PropertyType.Multilevel,
  dbValueType: StandardValueType.ListString,
  bizValueType: StandardValueType.ListListString,
  typeName: "Multilevel",
  poolName: "Custom",
  order: 12,
};

// ============================================
// Mock Filter Groups
// ============================================

/**
 * Comprehensive filter group that covers ALL scenarios:
 * - All property types (text, choice, number, date, tags, multilevel, etc.)
 * - Nested groups (3 levels deep)
 * - Both AND and OR combinators
 * - Various operations (Contains, Equals, GreaterThanOrEquals, LessThanOrEquals, etc.)
 *
 * Structure:
 * ROOT (AND)
 * ├── Title contains "keyword" (SingleLineText)
 * ├── Rating >= 3 (Rating/Number)
 * ├── Progress <= 80% (Percentage)
 * ├── Active = true (Boolean)
 * ├── GROUP 1 (OR) - Status/Choice filters
 * │   ├── Status = "published" (SingleChoice)
 * │   ├── Status = "draft" (SingleChoice)
 * │   └── GROUP 1.1 (AND) - Date range
 * │       ├── Release Date >= 2024-01-01
 * │       └── Release Date <= 2024-12-31
 * ├── GROUP 2 (OR) - Categories/MultipleChoice
 * │   ├── Categories contains ["action"]
 * │   └── Categories contains ["drama"]
 * ├── GROUP 3 (AND) - DateTime and Time
 * │   ├── Created At <= 2024-12-31 23:59:59 (DateTime)
 * │   └── Duration >= 01:30:00 (Time)
 * └── GROUP 4 (OR) - Tags and Location
 *     ├── Tags contains ["tag1", "tag3"]
 *     └── Location contains ["beijing"] (Multilevel)
 */
const createComprehensiveFilterGroup = (): SearchFilterGroup => ({
  combinator: GroupCombinator.And,
  disabled: false,
  filters: [
    // SingleLineText - Contains
    {
      propertyId: 1,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Contains,
      property: mockSingleLineTextProperty,
      valueProperty: mockSingleLineTextProperty,
      dbValue: serializeStandardValue("keyword", StandardValueType.String),
      disabled: false,
    },
    // MultilineText - Contains
    {
      propertyId: 2,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Contains,
      property: mockMultilineTextProperty,
      valueProperty: mockMultilineTextProperty,
      dbValue: serializeStandardValue("some long description text", StandardValueType.String),
      disabled: false,
    },
    // Rating >= 3
    {
      propertyId: 7,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.GreaterThanOrEquals,
      property: mockRatingProperty,
      valueProperty: mockRatingValueProperty,
      dbValue: serializeStandardValue(3, StandardValueType.Decimal),
      disabled: false,
    },
    // Percentage <= 80
    {
      propertyId: 6,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.LessThanOrEquals,
      property: mockPercentageProperty,
      valueProperty: mockPercentageProperty,
      dbValue: serializeStandardValue(80, StandardValueType.Decimal),
      disabled: false,
    },
    // Boolean = true
    {
      propertyId: 8,
      propertyPool: PropertyPool.Custom,
      operation: SearchOperation.Equals,
      property: mockBooleanProperty,
      valueProperty: mockBooleanProperty,
      dbValue: serializeStandardValue(true, StandardValueType.Boolean),
      disabled: false,
    },
  ],
  groups: [
    // GROUP 1: Status filters (OR combinator)
    {
      combinator: GroupCombinator.Or,
      disabled: false,
      filters: [
        // Status = published
        {
          propertyId: 3,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Equals,
          property: mockSingleChoiceProperty,
          valueProperty: mockSingleChoiceProperty,
          dbValue: serializeStandardValue("published", StandardValueType.String),
          disabled: false,
        },
        // Status = draft
        {
          propertyId: 3,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Equals,
          property: mockSingleChoiceProperty,
          valueProperty: mockSingleChoiceProperty,
          dbValue: serializeStandardValue("draft", StandardValueType.String),
          disabled: false,
        },
      ],
      groups: [
        // GROUP 1.1: Date range (AND combinator inside OR group - 3 levels deep)
        {
          combinator: GroupCombinator.And,
          disabled: false,
          filters: [
            // Release Date >= 2024-01-01
            {
              propertyId: 9,
              propertyPool: PropertyPool.Custom,
              operation: SearchOperation.GreaterThanOrEquals,
              property: mockDateProperty,
              valueProperty: mockDateProperty,
              dbValue: serializeStandardValue(dayjs("2024-01-01"), StandardValueType.DateTime),
              disabled: false,
            },
            // Release Date <= 2024-12-31
            {
              propertyId: 9,
              propertyPool: PropertyPool.Custom,
              operation: SearchOperation.LessThanOrEquals,
              property: mockDateProperty,
              valueProperty: mockDateProperty,
              dbValue: serializeStandardValue(dayjs("2024-12-31"), StandardValueType.DateTime),
              disabled: false,
            },
          ],
          groups: [],
        },
      ],
    },
    // GROUP 2: Categories (OR combinator)
    {
      combinator: GroupCombinator.Or,
      disabled: false,
      filters: [
        // Categories contains action
        {
          propertyId: 4,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockMultipleChoiceProperty,
          valueProperty: mockMultipleChoiceProperty,
          dbValue: serializeStandardValue(["action"], StandardValueType.ListString),
          disabled: false,
        },
        // Categories contains drama
        {
          propertyId: 4,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockMultipleChoiceProperty,
          valueProperty: mockMultipleChoiceProperty,
          dbValue: serializeStandardValue(["drama"], StandardValueType.ListString),
          disabled: false,
        },
      ],
      groups: [],
    },
    // GROUP 3: DateTime and Time (AND combinator)
    {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [
        // Created At <= 2024-12-31 23:59:59
        {
          propertyId: 10,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.LessThanOrEquals,
          property: mockDateTimeProperty,
          valueProperty: mockDateTimeProperty,
          dbValue: serializeStandardValue(dayjs("2024-12-31T23:59:59"), StandardValueType.DateTime),
          disabled: false,
        },
        // Duration >= 01:30:00
        {
          propertyId: 11,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.GreaterThanOrEquals,
          property: mockTimeProperty,
          valueProperty: mockTimeProperty,
          dbValue: serializeStandardValue(dayjs.duration({ hours: 1, minutes: 30 }), StandardValueType.Time),
          disabled: false,
        },
      ],
      groups: [],
    },
    // GROUP 4: Tags and Location (OR combinator)
    {
      combinator: GroupCombinator.Or,
      disabled: false,
      filters: [
        // Tags contains
        {
          propertyId: 12,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockTagsProperty,
          valueProperty: mockTagsProperty,
          dbValue: serializeStandardValue(["tag1", "tag3"], StandardValueType.ListString),
          disabled: false,
        },
        // Location contains beijing
        {
          propertyId: 13,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockMultilevelProperty,
          valueProperty: mockMultilevelProperty,
          dbValue: serializeStandardValue(["beijing"], StandardValueType.ListString),
          disabled: false,
        },
      ],
      groups: [],
    },
    // GROUP 5: Properties without options (AND combinator)
    {
      combinator: GroupCombinator.And,
      disabled: false,
      filters: [
        // SingleChoice without options
        {
          propertyId: 31,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Equals,
          property: mockSingleChoiceNoOptionsProperty,
          valueProperty: mockSingleChoiceNoOptionsProperty,
          dbValue: serializeStandardValue("some-value", StandardValueType.String),
          disabled: false,
        },
        // MultipleChoice without options
        {
          propertyId: 41,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockMultipleChoiceNoOptionsProperty,
          valueProperty: mockMultipleChoiceNoOptionsProperty,
          dbValue: serializeStandardValue(["value1", "value2"], StandardValueType.ListString),
          disabled: false,
        },
        // Tags without options
        {
          propertyId: 121,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockTagsNoOptionsProperty,
          valueProperty: mockTagsNoOptionsProperty,
          dbValue: serializeStandardValue(["tag-id-1"], StandardValueType.ListString),
          disabled: false,
        },
        // Multilevel without options
        {
          propertyId: 131,
          propertyPool: PropertyPool.Custom,
          operation: SearchOperation.Contains,
          property: mockMultilevelNoOptionsProperty,
          valueProperty: mockMultilevelNoOptionsProperty,
          dbValue: serializeStandardValue(["node-id-1"], StandardValueType.ListString),
          disabled: false,
        },
      ],
      groups: [],
    },
  ],
});

const ResourceFilterPage = () => {
  // Single comprehensive group state for all demos
  const [group, setGroup] = useState<SearchFilterGroup>(createComprehensiveFilterGroup);

  // State for portal demo
  const [portalGroup, setPortalGroup] = useState<SearchFilterGroup>(createComprehensiveFilterGroup);
  const [portalKeyword, setPortalKeyword] = useState<string | undefined>("");
  const [portalMode, setPortalMode] = useState<FilterDisplayMode>(FilterDisplayMode.Simple);

  // State for auto-create media library filter demo
  const [autoCreateGroupSimple, setAutoCreateGroupSimple] = useState<SearchFilterGroup | undefined>(undefined);
  const [autoCreateGroupAdvanced, setAutoCreateGroupAdvanced] = useState<SearchFilterGroup | undefined>(undefined);

  // Refs for portal containers
  const keywordContainerRef = useRef<HTMLDivElement>(null);
  const filterPortalContainerRef = useRef<HTMLDivElement>(null);
  const filterGroupsContainerRef = useRef<HTMLDivElement>(null);

  // Force re-render after refs are set
  const [refsReady, setRefsReady] = useState(false);
  useEffect(() => {
    setRefsReady(true);
  }, []);

  // Get the comprehensive group (static, no state needed for display-only)
  const comprehensiveGroup = createComprehensiveFilterGroup();

  return (
    <div className="p-4">
      <h1 className="text-2xl font-bold mb-2">ResourceFilter Test Page</h1>
      <p className="text-default-500 mb-4">
        Comprehensive test: All property types + Nested groups (3 levels) + AND/OR combinators
      </p>

      <Tabs aria-label="Filter test sections">
        {/* Comprehensive Comparison Tab - All 4 modes in one view */}
        <Tab key="comprehensive" title="All Modes Comparison">
          <div className="flex flex-col gap-6 pt-4">
            <p className="text-sm text-default-500">
              Same comprehensive data shown in all 4 mode combinations. Includes: SingleLineText, Rating, Percentage, Boolean,
              SingleChoice, MultipleChoice, Date, DateTime, Time, Tags, and Multilevel properties with nested groups.
            </p>

            {/* 2x2 Grid Layout */}
            <div className="grid grid-cols-2 gap-4">
              {/* Vertical + Simple */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Simple</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Horizontal + Simple */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Simple</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Vertical + Advanced */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Advanced</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Horizontal + Advanced */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Advanced</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>
            </div>
          </div>
        </Tab>

        {/* Interactive Tab */}
        <Tab key="interactive" title="Interactive">
          <div className="flex flex-col gap-6 pt-4">
            <p className="text-sm text-default-500">
              Interactive demo with the comprehensive filter group. Add, remove, and modify filters.
            </p>

            {/* 2x2 Grid Layout */}
            <div className="grid grid-cols-2 gap-4">
              {/* Vertical + Simple - Interactive */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Simple (Interactive)</div>
                <ResourceFilterController
                  group={group}
                  onGroupChange={setGroup}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Horizontal + Simple - Interactive */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Simple (Interactive)</div>
                <ResourceFilterController
                  group={group}
                  onGroupChange={setGroup}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Vertical + Advanced - Interactive */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Advanced (Interactive)</div>
                <ResourceFilterController
                  group={group}
                  onGroupChange={setGroup}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>

              {/* Horizontal + Advanced - Interactive */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Advanced (Interactive)</div>
                <ResourceFilterController
                  group={group}
                  onGroupChange={setGroup}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                />
              </div>
            </div>
          </div>
        </Tab>

        {/* Portal Demo Tab */}
        <Tab key="portal" title="Portal Demo">
          <div className="pt-4">
            <Card>
              <CardHeader>
                <span className="font-semibold">Parts Rendered to Different Containers</span>
              </CardHeader>
              <CardBody>
                <p className="text-sm text-default-500 mb-4">
                  This demo shows how ResourceFilterController can render different parts to separate containers.
                  The keyword input, filter button, and filter groups are each rendered to different areas.
                </p>

                <div className="grid grid-cols-3 gap-4 mb-4">
                  {/* Keyword Container */}
                  <div className="border rounded p-3">
                    <div className="text-xs text-default-400 mb-2">Keyword Container</div>
                    <div ref={keywordContainerRef} />
                  </div>

                  {/* Filter Portal Container */}
                  <div className="border rounded p-3">
                    <div className="text-xs text-default-400 mb-2">Filter Portal Container</div>
                    <div ref={filterPortalContainerRef} className="flex justify-center" />
                  </div>

                  {/* Mode indicator */}
                  <div className="border rounded p-3">
                    <div className="text-xs text-default-400 mb-2">Current Mode</div>
                    <div className="text-lg font-semibold">
                      {portalMode === FilterDisplayMode.Simple ? "Simple" : "Advanced"}
                    </div>
                  </div>
                </div>

                {/* Filter Groups Container */}
                <div className="border rounded p-3">
                  <div className="text-xs text-default-400 mb-2">Filter Groups Container (Scrollable)</div>
                  <div
                    ref={filterGroupsContainerRef}
                    className="max-h-[200px] overflow-y-auto"
                  />
                </div>

                {/* The controller renders parts to the containers above */}
                {refsReady && (
                  <ResourceFilterController
                    keyword={portalKeyword}
                    onKeywordChange={setPortalKeyword}
                    group={portalGroup}
                    onGroupChange={setPortalGroup}
                    filterDisplayMode={portalMode}
                    onFilterDisplayModeChange={setPortalMode}
                    filterLayout="vertical"
                    keywordContainer={keywordContainerRef.current}
                    filterPortalContainer={filterPortalContainerRef.current}
                    filterGroupsContainer={filterGroupsContainerRef.current}
                    keywordClassName="w-full"
                  />
                )}
              </CardBody>
            </Card>
          </div>
        </Tab>

        {/* Auto-Create Media Library Filter Tab */}
        <Tab key="auto-create" title="Auto-Create Filter">
          <div className="flex flex-col gap-6 pt-4">
            <p className="text-sm text-default-500">
              Tests the autoCreateMediaLibraryFilter prop. When enabled and group is empty/undefined,
              a media library filter is automatically created and onChange is triggered.
            </p>

            {/* 1x2 Grid Layout */}
            <div className="grid grid-cols-2 gap-4">
              {/* Simple Mode */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Simple Mode</div>
                <p className="text-xs text-default-400 mb-2">
                  Initial group: undefined. Should auto-create media library filter.
                </p>
                <ResourceFilterController
                  group={autoCreateGroupSimple}
                  onGroupChange={setAutoCreateGroupSimple}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                  autoCreateMediaLibraryFilter
                />
                <div className="mt-2 text-xs text-default-500">
                  Group state: {autoCreateGroupSimple ? `${autoCreateGroupSimple.filters?.length ?? 0} filter(s)` : "undefined"}
                </div>
              </div>

              {/* Advanced Mode */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Advanced Mode</div>
                <p className="text-xs text-default-400 mb-2">
                  Initial group: undefined. Should auto-create media library filter.
                </p>
                <ResourceFilterController
                  group={autoCreateGroupAdvanced}
                  onGroupChange={setAutoCreateGroupAdvanced}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                  autoCreateMediaLibraryFilter
                />
                <div className="mt-2 text-xs text-default-500">
                  Group state: {autoCreateGroupAdvanced ? `${autoCreateGroupAdvanced.filters?.length ?? 0} filter(s)` : "undefined"}
                </div>
              </div>
            </div>

            {/* JSON Preview */}
            <Card>
              <CardHeader>Auto-Created Filter Group JSON</CardHeader>
              <CardBody>
                <ReactJson
                  collapsed={2}
                  name={"autoCreateGroupSimple"}
                  src={autoCreateGroupSimple ?? {}}
                  theme={"monokai"}
                  style={{ fontSize: 11 }}
                />
              </CardBody>
            </Card>
          </div>
        </Tab>

        {/* Readonly Mode Tab */}
        <Tab key="readonly" title="Readonly Mode">
          <div className="flex flex-col gap-6 pt-4">
            <p className="text-sm text-default-500">
              Readonly mode displays filters without any action buttons (delete, disable, add filter, edit operations).
              Useful for displaying filter criteria in a non-editable view.
            </p>

            {/* 2x2 Grid Layout */}
            <div className="grid grid-cols-2 gap-4">
              {/* Vertical + Simple - Readonly */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Simple (Readonly)</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                  isReadonly
                />
              </div>

              {/* Horizontal + Simple - Readonly */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Simple (Readonly)</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                  isReadonly
                />
              </div>

              {/* Vertical + Advanced - Readonly */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-primary mb-2">Vertical + Advanced (Readonly)</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="vertical"
                  showRecentFilters={false}
                  showTags={false}
                  isReadonly
                />
              </div>

              {/* Horizontal + Advanced - Readonly */}
              <div className="border border-default-200 rounded p-3">
                <div className="font-semibold text-secondary mb-2">Horizontal + Advanced (Readonly)</div>
                <ResourceFilterController
                  group={comprehensiveGroup}
                  onGroupChange={() => {}}
                  filterDisplayMode={FilterDisplayMode.Advanced}
                  filterLayout="horizontal"
                  showRecentFilters={false}
                  showTags={false}
                  isReadonly
                />
              </div>
            </div>
          </div>
        </Tab>

        {/* JSON Data Tab */}
        <Tab key="json" title="JSON Data">
          <div className="pt-4">
            <Card>
              <CardHeader>Comprehensive Filter Group Structure</CardHeader>
              <CardBody>
                <ReactJson
                  collapsed={2}
                  name={"group"}
                  src={comprehensiveGroup}
                  theme={"monokai"}
                  style={{ fontSize: 11 }}
                />
              </CardBody>
            </Card>
          </div>
        </Tab>
      </Tabs>
    </div>
  );
};

ResourceFilterPage.displayName = "ResourceFilterPage";

export default ResourceFilterPage;
