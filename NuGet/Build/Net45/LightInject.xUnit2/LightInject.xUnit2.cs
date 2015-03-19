/*********************************************************************************   
    The MIT License (MIT)

    Copyright (c) 2014 bernhard.richter@gmail.com

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
******************************************************************************
    LightInject.xUnit version 2.0.0.0
    http://www.lightinject.net/
    http://twitter.com/bernhardrichter
******************************************************************************/
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1126:PrefixCallsCorrectly", Justification = "Reviewed")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:PrefixLocalCallsWithThis", Justification = "No inheritance")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleClass", Justification = "Single source file deployment.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1633:FileMustHaveHeader", Justification = "Custom header.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:ElementsMustBeDocumented", Justification = "All public members are documented.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Product name starts with lowercase letter.")]

namespace LightInject.xUnit2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Remoting.Messaging;

    using LightInject;

    using global::Xunit.Sdk;

    /// <summary>
    /// Allows LightInject to resolve test method arguments.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class InjectDataAttribute : DataAttribute
    {
        private const string Key = "XunitServiceContainer";

        /// <summary>
        /// Gets a list of argument values resolved using an <see cref="IServiceContainer"/> instance.
        /// </summary>
        /// <param name="methodUnderTest">The test method currently being executed.</param>        
        /// <returns>A list of argument values resolved using an <see cref="IServiceContainer"/> instance.</returns>
        public override IEnumerable<object[]> GetData(MethodInfo methodUnderTest)
        {
            var container = GetContainer(methodUnderTest.DeclaringType);
            ParameterInfo[] parameters = methodUnderTest.GetParameters();
            if (ShouldStartScope(methodUnderTest))
            {
                container.BeginScope();
            }

            try
            {
                return ResolveParameters(container, parameters);
            }
            catch (InvalidOperationException ex)
            {
                const string ErrorMessage = "Unable to inject test method arguments. "
                + "Create a static method in the test class with the following signature "
                + "to configure the container: public static void Configure(IServiceContainer container)";
                throw new InvalidOperationException(ErrorMessage, ex);
            }
        }

        /// <summary>
        /// Releases the current <see cref="Scope"/>.
        /// </summary>
        /// <param name="type">The test class <see cref="Type"/> for which to release the current <see cref="Scope"/>.</param>
        internal static void Release(Type type)
        {
            var key = CreateContainerKey(type);
            var containerWrapper = (ContainerWrapper)CallContext.LogicalGetData(key);

            if (containerWrapper != null)
            {
                containerWrapper.Value.EndCurrentScope();
            }
        }

        private static string CreateContainerKey(Type type)
        {
            return Key + type.AssemblyQualifiedName;
        }

        private static bool ShouldStartScope(MethodInfo methodUnderTest)
        {
            return methodUnderTest.IsDefined(typeof(ScopedAttribute), true);
        }

        private static IServiceContainer GetContainer(Type type)
        {
            var containerWrapper = (ContainerWrapper)CallContext.LogicalGetData(Key);
            if (containerWrapper == null)
            {
                containerWrapper = new ContainerWrapper { Value = new ServiceContainer() };
                InvokeConfigureMethodIfPresent(type, containerWrapper.Value);
                CallContext.LogicalSetData(Key, containerWrapper);
            }

            return containerWrapper.Value;
        }

        private static IEnumerable<object[]> ResolveParameters(IServiceFactory factory, IEnumerable<ParameterInfo> parameters)
        {
            return new[] { parameters.Select(p => GetInstance(factory, p)).ToArray() };
        }

        private static object GetInstance(IServiceFactory factory, ParameterInfo parameter)
        {
            var instance = factory.TryGetInstance(parameter.ParameterType);
            if (instance == null)
            {
                instance = factory.GetInstance(parameter.ParameterType, parameter.Name);
            }

            return instance;
        }

        private static void InvokeConfigureMethodIfPresent(Type type, IServiceContainer container)
        {
            var composeMethod = type.GetMethod(
                "Configure",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            if (composeMethod != null)
            {
                composeMethod.Invoke(null, new object[] { container });
            }
        }

        [Serializable]
        private class ContainerWrapper : MarshalByRefObject
        {
            [NonSerialized]
            private IServiceContainer value;

            public IServiceContainer Value
            {
                get
                {
                    return value;
                }

                set
                {
                    this.value = value;
                }
            }
        }
    }

    /// <summary>
    /// Indicates that a new <see cref="Scope"/> should be started for this test method.
    /// </summary>
    public class ScopedAttribute : BeforeAfterTestAttribute
    {
        /// <summary>
        /// This method is called after the test method is executed.
        /// </summary>
        /// <param name="methodUnderTest">The method under test</param>
        public override void After(MethodInfo methodUnderTest)
        {
            InjectDataAttribute.Release(methodUnderTest.DeclaringType);
        }
    }
}
