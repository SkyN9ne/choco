﻿// Copyright © 2017 - 2021 Chocolatey Software, Inc
// Copyright © 2011 - 2017 RealDimensions Software, LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
// You may obtain a copy of the License at
//
// 	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace chocolatey.tests.integration.scenarios
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Xml.XPath;
    using chocolatey.infrastructure.app.commands;
    using chocolatey.infrastructure.app.configuration;
    using chocolatey.infrastructure.app.services;
    using chocolatey.infrastructure.filesystem;
    using chocolatey.infrastructure.results;
    using NuGet.Configuration;
    using NuGet.Packaging;
    using NUnit.Framework;
    using Should;

    public class UpgradeScenarios
    {
        [ConcernFor("upgrade")]
        public abstract class ScenariosBase : TinySpec
        {
            protected ConcurrentDictionary<string, PackageResult> Results;
            protected ChocolateyConfiguration Configuration;
            protected IChocolateyPackageService Service;

            public override void Context()
            {
                Configuration = Scenario.upgrade();
                Scenario.reset(Configuration);
                Configuration.PackageNames = Configuration.Input = "upgradepackage";
                Scenario.add_packages_to_source_location(Configuration, Configuration.Input + "*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "installpackage*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "badpackage*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "installpackage", "1.0.0");
                Scenario.install_package(Configuration, "upgradepackage", "1.0.0");
                Configuration.SkipPackageInstallProvider = true;
                Scenario.install_package(Configuration, "badpackage", "1.0");
                Configuration.SkipPackageInstallProvider = false;

                Service = NUnitSetup.Container.GetInstance<IChocolateyPackageService>();
            }
        }

        public class when_noop_upgrading_a_package_that_has_available_upgrades : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Noop = true;
            }

            public override void Because()
            {
                Service.upgrade_noop(Configuration);
            }

            [Fact]
            public void should_contain_older_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.0.0");
            }

            [Fact]
            public void should_contain_a_message_that_a_new_version_is_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_a_package_can_be_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("can upgrade 1/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }
        }

        public class when_noop_upgrading_a_package_that_does_not_have_available_upgrades : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Noop = true;
                Configuration.PackageNames = Configuration.Input = "installpackage";
            }

            public override void Because()
            {
                Service.upgrade_noop(Configuration);
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("installpackage v1.0.0 is the latest version available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_can_be_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("can upgrade 0/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }
        }

        public class when_noop_upgrading_a_package_that_does_not_exist : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Noop = true;
                Configuration.PackageNames = Configuration.Input = "nonexistentpackage";
            }

            public override void Because()
            {
                Service.upgrade_noop(Configuration);
            }

            [Fact]
            public void should_contain_a_message_the_package_was_not_found()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Error).or_empty_list_if_null())
                {
                    if (message.Contains("nonexistentpackage not installed. The package was not found with the source(s) listed")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_can_be_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("can upgrade 0/0")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }
        }

        public class when_upgrading_an_existing_package_happy_path : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.0.0 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.0 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.0 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.0 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        [Categories.SourcePriority]
        public class when_upgrading_an_existing_package_with_higher_version_in_non_prioritised_source : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Sources = string.Join(",",
                    Scenario.add_packages_to_priority_source_location(Configuration, "upgradepackage.1.0.0" + NuGetConstants.PackageExtension, priority: 1),
                    Scenario.add_packages_to_priority_source_location(Configuration, "upgradepackage.1.1.0" + NuGetConstants.PackageExtension, priority: 0));
                Scenario.install_package(Configuration, "upgradepackage", "1.0.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.Select(r => r.Value).FirstOrDefault();
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("upgradepackage v1.0.0 is the latest version available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1 ")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFolder = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);

                using (var reader = new PackageFolderReader(packageFolder))
                {
                    reader.NuspecReader.GetVersion().ToNormalizedString().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_original_package_version()
            {
                _packageResult.Version.ShouldEqual("1.0.0");
            }
        }

        public class when_upgrading_an_existing_package_with_prerelease_available_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Scenario.install_package(Configuration, "upgradepackage", "1.1.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("upgradepackage v1.1.0 is the latest version available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1 ")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_original_package_version()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_an_existing_package_with_prerelease_available_and_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.to_string().ShouldEqual("1.1.1-beta2");
                    version.ToString().ShouldEqual("1.1.1-beta2");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.1-beta2 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_the_new_beta()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.0.0 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta2 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        [Categories.SemVer20]
        public class when_upgrading_an_existing_package_with_semver_2_0_prerelease_available_and_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.remove_packages_from_destination_location(Configuration, "upgradepackage.1.1.1-beta2.nupkg");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.to_string().ShouldEqual("1.1.1-beta.1");
                    version.ToString().ShouldEqual("1.1.1-beta.1");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.1-beta.1 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_the_new_beta()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.0.0 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta.1 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        public class when_upgrading_an_existing_prerelease_package_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Configuration.Prerelease = false;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();

                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.to_string().ShouldEqual("1.1.1-beta2");
                    version.to_string().ShouldEqual("1.1.1-beta2");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.1.1-beta installed. Version 1.1.1-beta2 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_the_new_beta()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.1.1-beta Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta2 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        [Categories.SemVer20]
        public class when_upgrading_an_existing_prerelease_package_to_semver_2_0_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Scenario.remove_packages_from_destination_location(Configuration, "upgradepackage.1.1.1-beta2.nupkg");
                Configuration.Prerelease = false;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();

                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.to_string().ShouldEqual("1.1.1-beta.1");
                    version.to_string().ShouldEqual("1.1.1-beta.1");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.1.1-beta installed. Version 1.1.1-beta.1 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_the_new_beta()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.1.1-beta Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta.1 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        [Categories.SemVer20]
        public class when_upgrading_an_existing_semver_2_0_prerelease_package_to_legacy_semver_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta.1");
                Configuration.Prerelease = false;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();

                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.to_string().ShouldEqual("1.1.1-beta2");
                    version.to_string().ShouldEqual("1.1.1-beta2");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.1.1-beta.1 installed. Version 1.1.1-beta2 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_the_new_beta()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.1.1-beta.1 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta2 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta.1 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.1-beta2 Installed", LogLevel.Info).ShouldBeTrue();
            }
        }

        public class when_upgrading_an_existing_prerelease_package_with_prerelease_available_with_excludeprerelease_and_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Configuration.Prerelease = false;
                Configuration.UpgradeCommand.ExcludePrerelease = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("upgradepackage v1.1.1-beta is newer")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1 ")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.ShouldEqual("1.1.1-beta");
                    version.to_string().ShouldEqual("1.1.1-beta");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_only_find_the_last_stable_version()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_an_existing_prerelease_package_with_allow_downgrade_with_excludeprerelease_and_without_prerelease_specified : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Configuration.Prerelease = false;
                Configuration.UpgradeCommand.ExcludePrerelease = true;
                Configuration.AllowDowngrade = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("upgradepackage v1.1.1-beta is newer")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1 ")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.ShouldEqual("1.1.1-beta");
                    version.to_string().ShouldEqual("1.1.1-beta");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_only_find_the_last_stable_version()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_force_upgrading_a_package : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Force = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_a_package_that_does_not_have_available_upgrades : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "installpackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("installpackage v1.0.0 is the latest version available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1 ")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_existing_version_of_one_dot_zero_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.0.0");
            }
        }

        public class when_force_upgrading_a_package_that_does_not_have_available_upgrades : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.Force = true;
                Configuration.PackageNames = Configuration.Input = "installpackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_contain_a_message_that_you_have_the_latest_version_available()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null())
                {
                    if (message.Contains("installpackage v1.0.0 is the latest version available based on your source(s)")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_the_package_was_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_create_a_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_not_remove_the_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_be_the_same_version_of_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_existing_version_of_one_dot_zero_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.0.0");
            }
        }

        public class when_upgrading_packages_with_packages_config : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                var packagesConfig = "{0}{1}context{1}testing.packages.config".format_with(Scenario.get_top_level(), Path.DirectorySeparatorChar);
                Configuration.PackageNames = Configuration.Input = packagesConfig;
            }

            public override void Because()
            {
            }

            [Fact]
            public void should_throw_an_error_that_it_is_not_allowed()
            {
                Action m = () => Service.upgrade_run(Configuration);

                m.ShouldThrow<ApplicationException>();
            }
        }

        [WindowsOnly]
        [Platform(Exclude = "Mono")]
        public class when_upgrading_a_package_with_readonly_files : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                var fileToSetReadOnly = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "chocolateyInstall.ps1");
                var fileSystem = new DotNetFileSystem();
                fileSystem.ensure_file_attribute_set(fileToSetReadOnly, FileAttributes.ReadOnly);
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }
        }

        [WindowsOnly]
        [Platform(Exclude = "Mono")]
        public class when_upgrading_a_package_with_a_read_and_delete_share_locked_file : ScenariosBase
        {
            private PackageResult _packageResult;

            private FileStream fileStream;

            public override void Context()
            {
                base.Context();
                var fileToOpen = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "chocolateyInstall.ps1");
                fileStream = new FileStream(fileToOpen, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
            }

            public override void AfterObservations()
            {
                base.AfterObservations();
                fileStream.Close();
                fileStream.Dispose();
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            [Pending("Does not work under .Net 4.8, See issue #2690")]
            [Broken]
            public void should_not_be_able_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }
        }

        [WindowsOnly]
        [Platform(Exclude = "Mono")]
        public class when_upgrading_a_package_with_an_exclusively_locked_file : ScenariosBase
        {
            private PackageResult _packageResult;

            private FileStream fileStream;

            public override void Context()
            {
                base.Context();
                var fileToOpen = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "chocolateyInstall.ps1");
                fileStream = new FileStream(fileToOpen, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }

            public override void AfterObservations()
            {
                base.AfterObservations();
                fileStream.Close();
                fileStream.Dispose();
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_have_a_package_installed_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_old_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.0.0");
            }

            [Fact]
            public void should_not_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_was_not_able_to_upgrade()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }
        }

        public class when_upgrading_a_package_with_added_files : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                var fileAdded = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "dude.txt");
                File.WriteAllText(fileAdded, "hellow");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_keep_the_added_file()
            {
                var fileAdded = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "dude.txt");

                FileAssert.Exists(fileAdded);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_a_package_with_changed_files : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                var fileChanged = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "chocolateyinstall.ps1");
                File.WriteAllText(fileChanged, "hellow");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_update_the_changed_file()
            {
                var fileChanged = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "chocolateyinstall.ps1");

                File.ReadAllText(fileChanged).ShouldNotEqual("hellow");
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_a_package_that_does_not_exist : ScenariosBase
        {
            private PackageResult packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "nonexistentpackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_not_install_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_message_the_package_was_not_found()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Error).or_empty_list_if_null())
                {
                    if (message.Contains("nonexistentpackage not installed. The package was not found with the source(s) listed")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_message_that_no_packages_were_upgraded()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_a_successful_package_result()
            {
                packageResult.Success.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_have_an_error_package_result()
            {
                bool errorFound = false;
                foreach (var message in packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }

            [Fact]
            public void should_have_expected_error_in_package_result()
            {
                bool errorFound = false;
                foreach (var message in packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        if (message.Message.Contains("The package was not found")) errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }
        }

        public class when_upgrading_a_package_that_is_not_installed : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "installpackage";
                Service.uninstall_run(Configuration);
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_install_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_install_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_not_have_a_rollback_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }
        }

        [WindowsOnly]
        [Platform(Exclude = "Mono")]
        public class when_upgrading_a_package_that_is_not_installed_and_failing_on_not_installed : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "installpackage";
                Service.uninstall_run(Configuration);
                Configuration.UpgradeCommand.FailOnNotInstalled = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_not_install_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_was_unable_to_upgrade_a_package()
            {
                bool notInstalled = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("0/1")) notInstalled = true;
                }

                notInstalled.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_have_an_error_package_result()
            {
                bool errorFound = false;
                foreach (var message in _packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }

            [Fact]
            public void should_have_expected_error_in_package_result()
            {
                bool errorFound = false;
                foreach (var message in _packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        if (message.Message.Contains("Cannot upgrade a non-existent package")) errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }
        }

        [WindowsOnly]
        [Platform(Exclude = "Mono")]
        public class when_upgrading_a_package_that_errors : ScenariosBase
        {
            private PackageResult packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "badpackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_not_remove_package_from_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_not_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_put_the_package_in_the_lib_bad_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bad", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_have_the_erroring_upgraded_package_in_the_lib_bad_directory()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib-bad", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.0.0");
                }
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_was_unable_to_upgrade_a_package()
            {
                bool installedSuccessfully = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("0/1")) installedSuccessfully = true;
                }

                installedSuccessfully.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_a_successful_package_result()
            {
                packageResult.Success.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_have_an_error_package_result()
            {
                bool errorFound = false;
                foreach (var message in packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }

            [Fact]
            public void should_have_expected_error_in_package_result()
            {
                bool errorFound = false;
                foreach (var message in packageResult.Messages)
                {
                    if (message.MessageType == ResultType.Error)
                    {
                        if (message.Message.Contains("chocolateyInstall.ps1")) errorFound = true;
                    }
                }

                errorFound.ShouldBeTrue();
            }
        }

        public class when_upgrading_a_package_with_dependencies_happy : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "hasdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_upgrade_the_minimum_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.0.0");
                }
            }

            [Fact]
            public void should_contain_a_message_that_everything_upgraded_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 3/3")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_package_with_unavailable_dependencies : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "hasdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency.1*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_not_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_minimum_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_contain_a_message_that_it_was_unable_to_upgrade_anything()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 0/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_have_an_error_package_result()
            {
                bool errorFound = false;

                foreach (var packageResult in Results)
                {
                    foreach (var message in packageResult.Value.Messages)
                    {
                        if (message.MessageType == ResultType.Error)
                        {
                            errorFound = true;
                        }
                    }
                }

                errorFound.ShouldBeTrue();
            }

            [Fact]
            public void should_have_expected_error_in_package_result()
            {
                bool errorFound = false;

                foreach (var packageResult in Results)
                {
                    foreach (var message in packageResult.Value.Messages)
                    {
                        if (message.MessageType == ResultType.Error)
                        {
                            if (message.Message.Contains("Unable to resolve dependency 'isexactversiondependency")) errorFound = true;
                        }
                    }
                }

                errorFound.ShouldBeTrue();
            }
        }

        public class when_upgrading_a_package_with_unavailable_dependencies_ignoring_dependencies : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "hasdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency.1*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
                Configuration.IgnoreDependencies = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_minimum_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_contain_a_message_that_it_upgraded_only_the_package_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_dependency_happy : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "isdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.1*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_parent_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_not_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_contain_a_message_the_dependency_upgraded_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_dependency_legacy_folder_version : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "isdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.1*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency*" +  NuGetConstants.PackageExtension);
                Configuration.AllowMultipleVersions = true;
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
                Configuration.AllowMultipleVersions = false;

                string dotChocolatey = Path.Combine(Scenario.get_top_level(), ".chocolatey");
                if (Directory.Exists(dotChocolatey))
                {
                    try
                    {
                        Directory.Delete(dotChocolatey, recursive: true);
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(2000);
                        Directory.Delete(dotChocolatey, recursive: true);
                    }
                }
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_remove_the_legacy_folder_version_of_the_package()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isdependency.1.0.0");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_replace_the_legacy_folder_version_of_the_package_with_a_lib_package_folder_that_has_no_version()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isdependency");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_not_upgrade_the_parent_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency.1.0.0", "hasdependency.1.0.0.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_not_add_a_versionless_parent_package_folder_to_the_lib_dir()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_leave_the_parent_package_as_legacy_folder()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency.1.0.0");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_not_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency.1.0.0", "isexactversiondependency.1.0.0.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.0.0");
                }
            }

            [Fact]
            public void should_leave_the_exact_version_package_as_legacy_folder()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency.1.0.0");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_not_add_a_versionless_exact_version_package_folder_to_the_lib_dir()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_message_the_dependency_upgraded_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_dependency_with_parent_that_depends_on_a_range_less_than_upgrade_version : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "isdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency*" +  NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_upgrade_the_parent_package_to_highest_version_that_meets_new_dependency_version()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.0.0");
                }
            }

            [Fact]
            public void should_contain_a_message_that_everything_upgraded_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 3/3")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_legacy_folder_dependency_with_parent_that_depends_on_a_range_less_than_upgrade_version : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "isdependency";
                Scenario.add_packages_to_source_location(Configuration, "hasdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isdependency.*" +  NuGetConstants.PackageExtension);
                Scenario.add_packages_to_source_location(Configuration, "isexactversiondependency*" +  NuGetConstants.PackageExtension);
                Configuration.AllowMultipleVersions = true;
                Scenario.install_package(Configuration, "isdependency", "1.0.0");
                Scenario.install_package(Configuration, "hasdependency", "1.0.0");
                Configuration.AllowMultipleVersions = false;

                string dotChocolatey = Path.Combine(Scenario.get_top_level(), ".chocolatey");
                if (Directory.Exists(dotChocolatey))
                {
                    Directory.Delete(dotChocolatey, recursive: true);
                }
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isdependency", "isdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_remove_the_legacy_folder_version_of_the_package()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isdependency.1.0.0");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_replace_the_legacy_folder_version_of_the_package_with_a_lib_package_folder_that_has_no_version()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isdependency");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_upgrade_the_parent_package_to_highest_version_that_meets_new_dependency_version()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency", "hasdependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.1.0");
                }
            }

            [Fact]
            public void should_replace_the_legacy_folder_version_of_the_parent_package_with_a_lib_package_folder_that_has_no_version()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            //[Pending("Legacy packages are left when implicit - GH-117")]
            public void should_remove_the_legacy_folder_version_of_the_parent_package()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "hasdependency.1.0.0");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_upgrade_the_exact_version_dependency()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency", "isexactversiondependency.nupkg");
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.0.0");
                }
            }

            [Fact]
            public void should_replace_the_legacy_folder_version_of_the_exact_version_package_with_a_lib_package_folder_that_has_no_version()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency");
                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            //[Pending("Legacy packages are left when implicit - GH-117")]
            public void should_remove_the_legacy_folder_version_of_the_exact_version_package()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", "isexactversiondependency.1.0.0");
                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_a_message_that_everything_upgraded_successfully()
            {
                bool expectedMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 3/3")) expectedMessage = true;
                }

                expectedMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Success.ShouldBeTrue();
                }
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Inconclusive.ShouldBeFalse();
                }
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                foreach (var packageResult in Results)
                {
                    packageResult.Value.Warning.ShouldBeFalse();
                }
            }
        }

        public class when_upgrading_a_package_with_config_transforms : ScenariosBase
        {
            private PackageResult _packageResult;
            private string _xmlFilePath = string.Empty;
            private XPathNavigator _xPathNavigator;

            public override void Context()
            {
                base.Context();

                _xmlFilePath = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe.config");
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;

                var xmlDocument = new XPathDocument(_xmlFilePath);
                _xPathNavigator = xmlDocument.CreateNavigator();
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }

            // any file in a nuget package will overwrite an existing file
            // on the file system - we subvert that by pulling the backup
            // if we've determined that there is an xdt file

            [Fact]
            public void should_not_change_the_test_value_in_the_config_from_original_one_dot_zero_dot_zero_due_to_upgrade_and_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='test']/@value").TypedValue.to_string().ShouldEqual("default 1.0.0");
            }

            [Fact]
            public void should_change_the_testReplace_value_in_the_config_due_to_XDT_Replace()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='testReplace']/@value").TypedValue.to_string().ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_not_change_the_insert_value_in_the_config_due_to_upgrade_and_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='insert']/@value").TypedValue.to_string().ShouldEqual("1.0.0");
            }

            [Fact]
            public void should_add_the_insertNew_value_in_the_config_due_to_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='insertNew']/@value").TypedValue.to_string().ShouldEqual("1.1.0");
            }
        }

        public class when_upgrading_a_package_with_config_transforms_when_config_was_edited : ScenariosBase
        {
            private PackageResult _packageResult;
            private string _xmlFilePath = string.Empty;
            private XPathNavigator _xPathNavigator;
            private const string COMMENT_ADDED = "<!-- dude -->";

            public override void Context()
            {
                base.Context();

                _xmlFilePath = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe.config");

                File.WriteAllText(_xmlFilePath, File.ReadAllText(_xmlFilePath) + COMMENT_ADDED);
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;

                var xmlDocument = new XPathDocument(_xmlFilePath);
                _xPathNavigator = xmlDocument.CreateNavigator();
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }

            // any file in a nuget package will overwrite an existing file
            // on the file system - we subvert that by pulling the backup
            // if we've determined that there is an xdt file

            [Fact]
            public void should_not_change_the_test_value_in_the_config_from_original_one_dot_zero_dot_zero_due_to_upgrade_and_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='test']/@value").TypedValue.to_string().ShouldEqual("default 1.0.0");
            }

            [Fact]
            public void should_change_the_testReplace_value_in_the_config_due_to_XDT_Replace()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='testReplace']/@value").TypedValue.to_string().ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_not_change_the_insert_value_in_the_config_due_to_upgrade_and_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='insert']/@value").TypedValue.to_string().ShouldEqual("1.0.0");
            }

            [Fact]
            public void should_add_the_insertNew_value_in_the_config_due_to_XDT_InsertIfMissing()
            {
                _xPathNavigator.SelectSingleNode("//configuration/appSettings/add[@key='insertNew']/@value").TypedValue.to_string().ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_have_a_config_with_the_comment_from_the_original()
            {
                File.ReadAllText(_xmlFilePath).ShouldContain(COMMENT_ADDED);
            }
        }

        public class when_upgrading_a_package_with_no_sources_enabled : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Sources = null;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_have_no_sources_enabled_result()
            {
                MockLogger.contains_message("Upgrading was NOT successful. There are no sources enabled for", LogLevel.Error).ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_any_packages_upgraded()
            {
                Results.Count().ShouldEqual(0);
            }
        }

        public class when_upgrading_all_packages_happy_path : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "all";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_report_for_all_installed_packages()
            {
                Results.Count().ShouldEqual(3);
            }

            [Fact]
            public void should_upgrade_packages_with_upgrades()
            {
                var upgradePackageResult = Results.Where(x => x.Key == "upgradepackage").ToList();
                upgradePackageResult.Count.ShouldEqual(1, "upgradepackage must be there once");
                upgradePackageResult.First().Value.Version.ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_skip_packages_without_upgrades()
            {
                var installPackageResult = Results.Where(x => x.Key == "installpackage").ToList();
                installPackageResult.Count.ShouldEqual(1, "installpackage must be there once");
                installPackageResult.First().Value.Version.ShouldEqual("1.0.0");
            }
        }

        public class when_upgrading_all_packages_with_prereleases_installed : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Configuration.Prerelease = false;
                Configuration.PackageNames = Configuration.Input = "all";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_report_for_all_installed_packages()
            {
                Results.Count().ShouldEqual(3);
            }

            [Fact]
            public void should_upgrade_packages_with_upgrades()
            {
                var upgradePackageResult = Results.Where(x => x.Key == "upgradepackage").ToList();
                upgradePackageResult.Count.ShouldEqual(1, "upgradepackage must be there once");
                upgradePackageResult.First().Value.Version.ShouldEqual("1.1.1-beta2");
            }

            [Fact]
            public void should_upgrade_upgradepackage()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "upgradepackage", "upgradepackage" +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.ShouldEqual("1.1.1-beta2");
                    version.to_string().ShouldEqual("1.1.1-beta2");
                }
            }

            [Fact]
            public void should_skip_packages_without_upgrades()
            {
                var installPackageResult = Results.Where(x => x.Key == "installpackage").ToList();
                installPackageResult.Count.ShouldEqual(1, "installpackage must be there once");
                installPackageResult.First().Value.Version.ShouldEqual("1.0.0");
            }
        }

        public class when_upgrading_all_packages_with_prereleases_installed_with_excludeprerelease_specified : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.Prerelease = true;
                Scenario.install_package(Configuration, "upgradepackage", "1.1.1-beta");
                Configuration.Prerelease = false;

                Configuration.PackageNames = Configuration.Input = "all";
                Configuration.UpgradeCommand.ExcludePrerelease = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_report_for_all_installed_packages()
            {
                Results.Count().ShouldEqual(3);
            }

            [Fact]
            public void should_upgrade_packages_with_upgrades()
            {
                var upgradePackageResult = Results.Where(x => x.Key == "upgradepackage").ToList();
                upgradePackageResult.Count.ShouldEqual(1, "upgradepackage must be there once");
                // available version will show as last stable
                upgradePackageResult.First().Value.Version.ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_not_upgrade_upgradepackage()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", "upgradepackage", "upgradepackage" +  NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    var version = packageReader.NuspecReader.GetVersion();
                    version.Version.to_string().ShouldEqual("1.1.1.0");
                    version.OriginalVersion.ShouldEqual("1.1.1-beta");
                    version.to_string().ShouldEqual("1.1.1-beta");
                }
            }

            [Fact]
            public void should_skip_packages_without_upgrades()
            {
                var installPackageResult = Results.Where(x => x.Key == "installpackage").ToList();
                installPackageResult.Count.ShouldEqual(1, "installpackage must be there once");
                installPackageResult.First().Value.Version.ShouldEqual("1.0.0");
            }
        }

        [Categories.SemVer20]
        public class when_upgrading_package_to_an_explicit_semver_2_0_version : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "upgradepackage";
                Configuration.Version = "1.1.1-beta.1";
                Configuration.Prerelease = true;
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.Select(r => r.Value).FirstOrDefault();
            }

            [Fact]
            public void should_have_a_single_package_result()
            {
                Results.Count.ShouldEqual(1, "The returned package results do not have a single value!");
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                FileAssert.Exists(shimFile);

                File.ReadAllText(shimFile).ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.1-beta.1");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.1-beta.1 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.1-beta.1");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.0.0 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.1-beta.1 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.0 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                const string expectedMessage = "upgradepackage 1.1.1-beta.1 Installed";

                MockLogger.contains_message(expectedMessage, LogLevel.Info).ShouldBeTrue("No log message containing the sentence '{0}' could be found!".format_with(expectedMessage));
            }
        }

        public class when_upgrading_all_packages_with_except : ScenariosBase
        {
            public override void Context()
            {
                base.Context();
                Configuration.PackageNames = Configuration.Input = "all";
                Configuration.UpgradeCommand.PackageNamesToSkip = "upgradepackage,badpackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
            }

            [Fact]
            public void should_report_for_all_non_skipped_packages()
            {
                Results.Count().ShouldEqual(1);
                Results.First().Key.ShouldEqual("installpackage");
            }

            [Fact]
            public void should_skip_packages_in_except_list()
            {
                var upgradePackageResult = Results.Where(x => x.Key == "upgradepackage").ToList();
                upgradePackageResult.Count.ShouldEqual(0, "upgradepackage should not be in the results list");
            }
        }

        public class when_upgrading_an_existing_hook_package : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Scenario.add_packages_to_source_location(Configuration, "scriptpackage.hook" + ".1.0.0" + NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "scriptpackage.hook", "1.0.0");
                Configuration.PackageNames = Configuration.Input = "scriptpackage.hook";
                Scenario.add_packages_to_source_location(Configuration, Configuration.Input + ".2.0.0" + NuGetConstants.PackageExtension);
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("2.0.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have scriptpackage.hook v1.0.0 installed. Version 2.0.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_two_dot_zero_dot_zero()
            {
                _packageResult.Version.ShouldEqual("2.0.0");
            }

            [Fact]
            public void should_have_a_hooks_folder_for_the_package()
            {
                var hooksDirectory = Path.Combine(Scenario.get_top_level(), "hooks", Configuration.PackageNames.Replace(".hook", string.Empty));

                DirectoryAssert.Exists(hooksDirectory);
            }

            [Fact]
            public void should_install_hook_scripts_to_folder()
            {
                var hookScripts = new List<string> { "pre-install-all.ps1", "post-install-all.ps1", "pre-upgrade-all.ps1", "post-upgrade-all.ps1", "pre-uninstall-all.ps1", "post-uninstall-all.ps1" };
                foreach (string scriptName in hookScripts)
                {
                    var hookScriptPath = Path.Combine(Scenario.get_top_level(), "hooks", Configuration.PackageNames.Replace(".hook", string.Empty), scriptName);
                    File.ReadAllText(hookScriptPath).ShouldContain("Write-Output");
                }
            }

            [Fact]
            public void should_remove_files_not_in_upgrade_version()
            {
                var hookScriptPath = Path.Combine(Scenario.get_top_level(), "hooks", Configuration.PackageNames.Replace(".hook", string.Empty), "pre-install-doesnotexist.ps1");
                FileAssert.DoesNotExist(hookScriptPath);
            }

            [Fact]
            public void should_install_new_files_in_upgrade_version()
            {
                var hookScriptPath = Path.Combine(Scenario.get_top_level(), "hooks", Configuration.PackageNames.Replace(".hook", string.Empty), "post-install-doesnotexist.ps1");
                FileAssert.Exists(hookScriptPath);
            }
        }

        public class when_upgrading_an_existing_package_happy_path_with_hooks : ScenariosBase
        {
            private PackageResult _packageResult;

            public override void Context()
            {
                base.Context();
                Scenario.add_packages_to_source_location(Configuration, "scriptpackage.hook" + "*" + NuGetConstants.PackageExtension);
                Scenario.install_package(Configuration, "scriptpackage.hook", "1.0.0");
                Configuration.PackageNames = Configuration.Input = "upgradepackage";
            }

            public override void Because()
            {
                Results = Service.upgrade_run(Configuration);
                _packageResult = Results.FirstOrDefault().Value;
            }

            [Fact]
            public void should_upgrade_where_install_location_reports()
            {
                DirectoryAssert.Exists(_packageResult.InstallLocation);
            }

            [Fact]
            public void should_upgrade_a_package_in_the_lib_directory()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames);

                DirectoryAssert.Exists(packageDir);
            }

            [Fact]
            public void should_delete_the_rollback()
            {
                var packageDir = Path.Combine(Scenario.get_top_level(), "lib-bkp", Configuration.PackageNames);

                DirectoryAssert.DoesNotExist(packageDir);
            }

            [Fact]
            public void should_contain_newer_version_in_directory()
            {
                var shimFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, "tools", "console.exe");

                File.ReadAllText(shimFile).ShouldEqual("1.1.0");
            }

            [Fact]
            public void should_upgrade_the_package()
            {
                var packageFile = Path.Combine(Scenario.get_top_level(), "lib", Configuration.PackageNames, Configuration.PackageNames + NuGetConstants.PackageExtension);
                using (var packageReader = new PackageArchiveReader(packageFile))
                {
                    packageReader.NuspecReader.GetVersion().to_string().ShouldEqual("1.1.0");
                }
            }

            [Fact]
            public void should_contain_a_warning_message_that_it_upgraded_successfully()
            {
                bool upgradedSuccessMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("upgraded 1/1")) upgradedSuccessMessage = true;
                }

                upgradedSuccessMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_contain_a_warning_message_with_old_and_new_versions()
            {
                bool upgradeMessage = false;
                foreach (var message in MockLogger.MessagesFor(LogLevel.Warn).or_empty_list_if_null())
                {
                    if (message.Contains("You have upgradepackage v1.0.0 installed. Version 1.1.0 is available based on your source")) upgradeMessage = true;
                }

                upgradeMessage.ShouldBeTrue();
            }

            [Fact]
            public void should_have_a_successful_package_result()
            {
                _packageResult.Success.ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_inconclusive_package_result()
            {
                _packageResult.Inconclusive.ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_warning_package_result()
            {
                _packageResult.Warning.ShouldBeFalse();
            }

            [Fact]
            public void config_should_match_package_result_name()
            {
                _packageResult.Name.ShouldEqual(Configuration.PackageNames);
            }

            [Fact]
            public void should_match_the_upgrade_version_of_one_dot_one_dot_zero()
            {
                _packageResult.Version.ShouldEqual("1.1.0");
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Before Modification", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyBeforeModify_before_chocolateyInstall()
            {
                MockLogger.MessagesFor(LogLevel.Info).or_empty_list_if_null()
                    .SkipWhile(p => !p.Contains("upgradepackage 1.0.0 Before Modification"))
                    .Any(p => p.EndsWith("upgradepackage 1.1.0 Installed"))
                    .ShouldBeTrue();
            }

            [Fact]
            public void should_not_have_executed_chocolateyUninstall_script_for_original_package()
            {
                MockLogger.contains_message("upgradepackage 1.0.0 Uninstalled", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            public void should_not_have_executed_chocolateyBeforeModify_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.0 Before Modification", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_chocolateyInstall_script_for_new_package()
            {
                MockLogger.contains_message("upgradepackage 1.1.0 Installed", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_pre_all_hook_script()
            {
                MockLogger.contains_message("pre-install-all.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_post_all_hook_script()
            {
                MockLogger.contains_message("post-install-all.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_pre_upgradepackage_hook_script()
            {
                MockLogger.contains_message("pre-install-upgradepackage.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_post_upgradepackage_hook_script()
            {
                MockLogger.contains_message("post-install-upgradepackage.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_not_have_executed_uninstall_hook_script()
            {
                MockLogger.contains_message("post-uninstall-all.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_not_have_executed_installpackage_hook_script()
            {
                MockLogger.contains_message("pre-install-installpackage.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeFalse();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_have_executed_beforemodify_hook_script_for_previous_version()
            {
                MockLogger.contains_message("pre-beforemodify-all.ps1 hook ran for upgradepackage 1.0.0", LogLevel.Info).ShouldBeTrue();
            }

            [Fact]
            [WindowsOnly]
            [Platform(Exclude = "Mono")]
            public void should_not_have_executed_beforemodify_hook_script_for_upgrade_version()
            {
                MockLogger.contains_message("pre-beforemodify-all.ps1 hook ran for upgradepackage 1.1.0", LogLevel.Info).ShouldBeFalse();
            }
        }
    }
}
