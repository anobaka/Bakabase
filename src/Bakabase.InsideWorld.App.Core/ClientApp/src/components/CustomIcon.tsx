import React, { Component } from 'react';
import DynamicIcon from '@icedesign/dynamic-icon';
import iconfont from '@/assets/iconfont/iconfont.css';

// 使用 custom 生成自定义 ICON 组件
const CustomIcon = DynamicIcon.create({
  fontFamily: 'iconfont',
  prefix: 'icon',
  css: iconfont,
  // css: '//at.alicdn.com/t/font_2282961_zu63tekgur.css',
});

export default CustomIcon;
