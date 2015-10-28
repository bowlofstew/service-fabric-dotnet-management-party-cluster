// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Fabric;
using System.Fabric.Description;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Diagnostics.EventListeners;
using System.Collections.ObjectModel;

namespace Microsoft.Diagnostics.EventListeners.Fabric
{
    public class FabricConfigurationProvider : IConfigurationProvider
    {
        public event EventHandler ConfigurationChanged;

        private KeyedCollection<string, ConfigurationProperty> configurationProperties;
        private string configurationSectionName;

        public FabricConfigurationProvider(string configurationSectionName)
        {
            if (string.IsNullOrWhiteSpace(configurationSectionName))
            {
                throw new ArgumentNullException("configurationSectionName");
            }
            this.configurationSectionName = configurationSectionName;

            CodePackageActivationContext activationContext = FabricRuntime.GetActivationContext();
            activationContext.ConfigurationPackageModifiedEvent += OnFabricConfigurationChanged;
            ConfigurationPackage configPackage = activationContext.GetConfigurationPackageObject("Config");
            UseConfiguration(configPackage);
        }

        public string GetValue(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var cachedConfigurationProperties = this.configurationProperties;
            if (cachedConfigurationProperties == null || !cachedConfigurationProperties.Contains(name))
            {
                return null;
            }
            else
            {
                return cachedConfigurationProperties[name].Value;
            }
        }

        private void UseConfiguration(ConfigurationPackage configPackage)
        {
            if (!configPackage.Settings.Sections.Contains(this.configurationSectionName))
            {
                this.configurationProperties = null;
            }
            else
            {
                this.configurationProperties = configPackage.Settings.Sections[this.configurationSectionName].Parameters;
            }
        }

        private void OnFabricConfigurationChanged(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            UseConfiguration(e.NewPackage);
            var configurationChanged = this.ConfigurationChanged;
            if (configurationChanged != null)
            {
                configurationChanged(this, EventArgs.Empty);
            }
        }
    }
}
