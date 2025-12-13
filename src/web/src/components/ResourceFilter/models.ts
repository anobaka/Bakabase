"use strict";

import type { ReactNode } from "react";
import type { IProperty } from "@/components/Property/models";
import type { PropertyPool, SearchOperation } from "@/sdk/constants";

export enum GroupCombinator {
  And = 1,
  Or = 2,
}

/**
 * 通用筛选器类型
 */
export type SearchFilter = {
  propertyId?: number;
  propertyPool?: PropertyPool;
  operation?: SearchOperation;
  dbValue?: string;
  bizValue?: string;
  availableOperations?: SearchOperation[];
  property?: IProperty;
  valueProperty?: IProperty;
  disabled: boolean;
};

/**
 * 通用筛选器分组类型
 */
export type SearchFilterGroup = {
  groups?: SearchFilterGroup[];
  filters?: SearchFilter[];
  combinator: GroupCombinator;
  disabled: boolean;
};

/**
 * API 适配器接口 - 抽象 API 调用
 */
export interface FilterApiAdapter {
  /**
   * 获取属性可用的操作列表
   */
  getAvailableOperations: (
    propertyPool: PropertyPool,
    propertyId: number
  ) => Promise<SearchOperation[]>;

  /**
   * 获取用于渲染值的属性定义
   */
  getValueProperty: (filter: SearchFilter) => Promise<IProperty | undefined>;

  /**
   * 保存最近使用的筛选器
   */
  saveRecentFilter: (filter: SearchFilter) => Promise<void>;

  /**
   * 获取最近使用的筛选器列表
   */
  getRecentFilters: () => Promise<SearchFilter[]>;
}

/**
 * 组件渲染器接口 - 抽象组件依赖
 */
export interface FilterComponentRenderers {
  /**
   * 打开属性选择器
   * @param currentSelection 当前选中的属性
   * @param onSelect 选择回调
   * @param onCancel 取消选择回调（关闭选择器但未选择）
   */
  openPropertySelector: (
    currentSelection: { id: number; pool: PropertyPool } | undefined,
    onSelect: (property: IProperty, availableOperations: SearchOperation[]) => void,
    onCancel?: () => void
  ) => void;

  /**
   * 渲染属性值输入
   */
  renderValueInput: (
    property: IProperty,
    dbValue: string | undefined,
    bizValue: string | undefined,
    onValueChange: (dbValue?: string, bizValue?: string) => void,
    options?: {
      defaultEditing?: boolean;
      size?: "sm" | "md" | "lg";
      variant?: "default" | "light";
      isReadonly?: boolean;
      /** Search operation - used for determining single/multiple selection for some property types */
      operation?: SearchOperation;
    }
  ) => ReactNode;
}

/**
 * 完整的 Filter 配置
 */
export interface FilterConfig {
  api: FilterApiAdapter;
  renderers: FilterComponentRenderers;
}

// 向后兼容的类型别名
export type ResourceSearchFilter = SearchFilter;
export type ResourceSearchFilterGroup = SearchFilterGroup;
