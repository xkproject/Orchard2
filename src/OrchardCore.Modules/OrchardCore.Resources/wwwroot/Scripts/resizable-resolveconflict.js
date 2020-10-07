/*
** NOTE: This file is generated by Gulp and should not be edited directly!
** Any changes made directly to this file will be overwritten next time its asset group is processed by Gulp.
*/

function _typeof(obj) { "@babel/helpers - typeof"; if (typeof Symbol === "function" && typeof Symbol.iterator === "symbol") { _typeof = function _typeof(obj) { return typeof obj; }; } else { _typeof = function _typeof(obj) { return obj && typeof Symbol === "function" && obj.constructor === Symbol && obj !== Symbol.prototype ? "symbol" : typeof obj; }; } return _typeof(obj); }

(function (factory, define, require, module) {
  'use strict';

  if (typeof define === 'function' && define.amd) {
    // AMD
    define(['jquery'], factory);
  } else if (_typeof(module) === 'object' && _typeof(module.exports) === 'object') {
    // CommonJS
    module.exports = factory(require('jquery'));
  } else {
    // Global jQuery
    factory(jQuery);
  }
})(function ($) {
  'use strict'; // rename to avoid conflict with jquery-resizable

  $.fn.uiresizable = $.fn.resizable;
  delete $.fn.resizable;
});