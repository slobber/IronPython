// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using IronPython.Runtime.Binding;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

namespace IronPython.Runtime {
#nullable enable
#pragma warning disable IPY01
    public abstract class MethodDescriptorBase : PythonTypeSlot, IDynamicMetaObjectProvider, IPythonExpandable {
        private readonly PythonDictionary _customAttributes = new();

        protected MethodDescriptorBase(CodeContext context, object func) {
            Context = context;
            _func = func;
            InitializeMetadata();
        }

        internal object _func;
        protected CodeContext Context { get; }

        private void InitializeMetadata() {
            CopyFunctionAttribute("__module__");
            CopyFunctionAttribute("__name__");
            CopyFunctionAttribute("__qualname__");
            CopyFunctionAttribute("__doc__");
        }

        private void CopyFunctionAttribute(string name) {
            if (PythonOps.TryGetBoundAttr(_func, name, out object? value)) {
                EnsureCustomAttributes()[name] = value;
            }
        }

        private object? GetFunctionAttribute(string name, bool cache) {
            if (_customAttributes.TryGetValue(name, out object? value)) {
                return value;
            }

            if (!PythonOps.TryGetBoundAttr(_func, name, out value)) {
                throw PythonOps.AttributeError("'{0}' object has no attribute '{1}'",
                    PythonOps.GetPythonTypeName(this), name);
            }

            if (cache) {
                _customAttributes[name] = value;
            }
            return value;
        }

        private void SetFunctionAttribute(string name, object? value) {
            EnsureCustomAttributes()[name] = value;
        }

        public object? __wrapped__ => _func;

        public object? __module__ {
            get => GetFunctionAttribute("__module__", cache: false);
            set => SetFunctionAttribute("__module__", value);
        }

        public object? __name__ {
            get => GetFunctionAttribute("__name__", cache: false);
            set => SetFunctionAttribute("__name__", value);
        }

        public object? __qualname__ {
            get => GetFunctionAttribute("__qualname__", cache: false);
            set => SetFunctionAttribute("__qualname__", value);
        }

        public object? __doc__ {
            get => GetFunctionAttribute("__doc__", cache: false);
            set => SetFunctionAttribute("__doc__", value);
        }

        public object? __annotations__ {
            get => GetFunctionAttribute("__annotations__", cache: true);
            set => SetFunctionAttribute("__annotations__", value);
        }

        public object? __annotate__ {
            get => GetFunctionAttribute("__annotate__", cache: true);
            set => SetFunctionAttribute("__annotate__", value);
        }

        public PythonDictionary __dict__ => EnsureCustomAttributes();

        private PythonDictionary EnsureCustomAttributes() {
            return _customAttributes;
        }

        IDictionary<object, object> IPythonExpandable.EnsureCustomAttributes() => EnsureCustomAttributes();

        IDictionary<object, object> IPythonExpandable.CustomAttributes => _customAttributes;

        CodeContext IPythonExpandable.Context => Context;

        DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter) {
            return GetMetaObjectCore(parameter);
        }

        protected abstract DynamicMetaObject GetMetaObjectCore(Expression parameter);
    }

    [PythonType]
    public class staticmethod : MethodDescriptorBase {

        public staticmethod(CodeContext/*!*/ context, object func) : base(context, func) {
        }

        public void __init__(CodeContext/*!*/ context, object func) {
            _func = func;
        }

        internal override bool TryGetValue(CodeContext context, object instance, PythonType owner, out object value) {
            value = __get__(instance, owner);
            return true;
        }

        internal override bool GetAlwaysSucceeds {
            get {
                return true;
            }
        }

        public object __func__ {
            get {
                return _func;
            }
        }

        public bool __isabstractmethod__ {
            get {
                object? isabstract;
                if (PythonOps.TryGetBoundAttr(_func, "__isabstractmethod__", out isabstract)) {
                    return PythonOps.IsTrue(isabstract);
                }
                return false;
            }
        }

        #region IDescriptor Members

        public object __get__(object? instance) { return __get__(instance, null); }

        public object __get__(object? instance, object? owner) {
            return _func;
        }

        protected override DynamicMetaObject GetMetaObjectCore(Expression parameter) {
            return new MetaExpandable<staticmethod>(parameter, this);
        }

        #endregion
    }

    [PythonType]
    public class classmethod : MethodDescriptorBase {

        public classmethod(CodeContext/*!*/ context, object func) : base(context, func) {
        }

        public void __init__(CodeContext/*!*/ context, object func) {
            _func = func;
        }

        internal override bool TryGetValue(CodeContext context, object instance, PythonType owner, out object value) {
            value = __get__(instance, owner);
            return true;
        }

        internal override bool GetAlwaysSucceeds => true;

        public object __func__ => _func;

        public bool __isabstractmethod__ {
            get {
                if (PythonOps.TryGetBoundAttr(_func, "__isabstractmethod__", out object? isabstract)) {
                    return PythonOps.IsTrue(isabstract);
                }
                return false;
            }
        }

        #region IDescriptor Members

        public object __get__(object? instance, object? owner = null) {
            if (owner == null) {
                if (instance == null) throw PythonOps.TypeError("__get__(None, None) is invalid");

                if (instance is int) {
                    // IronPython uses Int32 objects as "int" for performance reasons (GH #52)
                    owner = TypeCache.BigInteger;
                } else {
                    owner = DynamicHelpers.GetPythonType(instance);
                }
            }
            return new Method(_func, owner);
        }

        protected override DynamicMetaObject GetMetaObjectCore(Expression parameter) {
            return new MetaExpandable<classmethod>(parameter, this);
        }

        #endregion
    }
#pragma warning restore IPY01
#nullable restore

    [PythonType("property")]
    public class PythonProperty : PythonTypeDataSlot {
        private object _fget, _fset, _fdel, _doc;

        public PythonProperty() { }

        public PythonProperty([NotNone] params object[] args) { }

        public PythonProperty([ParamDictionary] IDictionary<object, object> dict, [NotNone] params object[] args) { }

        public void __init__(object fget = null, object fset = null, object fdel = null, object doc = null) {
            _fget = fget; _fset = fset; _fdel = fdel; _doc = doc;

            if (doc is null && GetType() != typeof(PythonProperty) && _fget is PythonFunction pfget) {
                // http://bugs.python.org/issue5890
                PythonDictionary dict = UserTypeOps.GetDictionary((IPythonObject)this);
                if (dict is null) {
                    throw PythonOps.AttributeError("'{0}' object attribute '__doc__' is read-only", PythonOps.GetPythonTypeName(this));
                }

                dict["__doc__"] = pfget.__doc__;
            }
        }

        internal override bool TryGetValue(CodeContext context, object instance, PythonType owner, out object value) {
            value = __get__(context, instance, owner);
            return true;
        }

        internal override bool GetAlwaysSucceeds {
            get {
                return true;
            }
        }

        internal override bool TrySetValue(CodeContext context, object instance, PythonType owner, object value) {
            if (instance == null) {
                return false;
            }
            __set__(context, instance, value);
            return true;
        }

        internal override bool TryDeleteValue(CodeContext context, object instance, PythonType owner) {
            if (instance == null) {
                return false;
            } 
            __delete__(context, instance);
            return true;
        }

        public bool __isabstractmethod__ {
            get {
                object isabstract;
                if (PythonOps.TryGetBoundAttr(_fget, "__isabstractmethod__", out isabstract)) {
                    return PythonOps.IsTrue(isabstract);
                } else if(PythonOps.TryGetBoundAttr(_fset, "__isabstractmethod__", out isabstract)) {
                    return PythonOps.IsTrue(isabstract);
                } else if(PythonOps.TryGetBoundAttr(_fdel, "__isabstractmethod__", out isabstract)) {
                    return PythonOps.IsTrue(isabstract);
                }
                return false;
            }
        }

        [SpecialName, PropertyMethod, WrapperDescriptor]
        public static object Get__doc__(CodeContext context, PythonProperty self) {
            if (self._doc is null && PythonOps.TryGetBoundAttr(self._fget, "__doc__", out var doc)) return doc;
            return self._doc;
        }

        [SpecialName, PropertyMethod, WrapperDescriptor]
        public static void Set__doc__(PythonProperty self, object value) {
            self._doc = value;
        }

        public object fdel {
            get { return _fdel; }
            set {
                throw PythonOps.AttributeError("readonly attribute");
            }
        }

        public object fset {
            get { return _fset; }
            set {
                throw PythonOps.AttributeError("readonly attribute");
            }
        }

        public object fget {
            get { return _fget; }
            set {
                throw PythonOps.AttributeError("readonly attribute");
            }
        }

        public override object __get__(CodeContext/*!*/ context, object instance, object owner=null) {
            if (instance == null) {
                return this;
            } else if (fget != null) {
                var site = context.LanguageContext.PropertyGetSite;

                return site.Target(site, context, fget, instance);
            }
            throw PythonOps.UnreadableProperty();
        }

        public override void __set__(CodeContext/*!*/ context, object instance, object value) {
            if (fset != null) {
                var site = context.LanguageContext.PropertySetSite;

                site.Target(site, context, fset, instance, value);
            } else {
                throw PythonOps.UnsetableProperty();
            }
        }

        public override void __delete__(CodeContext/*!*/ context, object instance) {
            if (fdel != null) {
                var site = context.LanguageContext.PropertyDeleteSite;

                site.Target(site, context, fdel, instance);
            } else {
                throw PythonOps.UndeletableProperty();
            }
        }

        public PythonProperty getter(object fget) {
            PythonProperty res = new PythonProperty();
            res.__init__(fget, _fset, _fdel, _doc);
            return res;
        }

        public PythonProperty setter(object fset) {
            PythonProperty res = new PythonProperty();
            res.__init__(_fget, fset, _fdel, _doc);
            return res;
        }

        public PythonProperty deleter(object fdel) {
            PythonProperty res = new PythonProperty();
            res.__init__(_fget, _fset, fdel, _doc);
            return res;
        }
    }

}
