﻿// Copyright 2016-2019, Pulumi Corporation.  All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;

using Pulumi;
using Pulumi.Azure.Compute;
using Pulumi.Azure.Compute.Inputs;
using Pulumi.Azure.Core;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;

class Program
{
    static Task<int> Main(string[] args)
    {
        return Deployment.RunAsync(() => {
            var resourceGroup = new ResourceGroup("server-rg");

            var network = new VirtualNetwork("server-network",
                new VirtualNetworkArgs
                {
                    ResourceGroupName = resourceGroup.Name,
                    AddressSpaces = { "10.0.0.0/16" },
                    Subnets =
                    {
                        new VirtualNetworkSubnetsArgs { Name = "default",  AddressPrefix = "10.0.1.0/24" }
                    },
                }
            );

            var publicIp = new PublicIp("server-ip",
                new PublicIpArgs
                {
                    ResourceGroupName = resourceGroup.Name,
                    AllocationMethod = "Dynamic",
                });

            var networkInterface = new NetworkInterface("server-nic",
                new NetworkInterfaceArgs
                {
                    ResourceGroupName = resourceGroup.Name,
                    IpConfigurations =
                    {
                        new NetworkInterfaceIpConfigurationsArgs
                        {
                            Name = "webserveripcfg",
                            SubnetId = network.Subnets.Apply(subnets => subnets[0].Id),
                            PrivateIpAddressAllocation = "dynamic",
                            PublicIpAddressId = publicIp.Id,
                        },
                    }
                });

            var vm = new VirtualMachine("server-vm",
                new VirtualMachineArgs
                {
                    ResourceGroupName = resourceGroup.Name,
                    NetworkInterfaceIds = { networkInterface.Id },
                    VmSize = "Standard_A0",
                    DeleteDataDisksOnTermination = true,
                    DeleteOsDiskOnTermination = true,
                    OsProfile = new VirtualMachineOsProfileArgs
                    {
                        ComputerName = "hostname",
                        AdminUsername = "testadmin",
                        AdminPassword = "Password1234!",
                        CustomData = 
@"#!/bin/bash\n
echo ""Hello, World!"" > index.html
nohup python -m SimpleHTTPServer 80 &",
                    },
                    OsProfileLinuxConfig = new VirtualMachineOsProfileLinuxConfigArgs
                    {
                        DisablePasswordAuthentication = false,
                    },
                    StorageOsDisk = new VirtualMachineStorageOsDiskArgs
                    {
                        CreateOption = "FromImage",
                        Name = "myosdisk1",
                    },
                    StorageImageReference = new VirtualMachineStorageImageReferenceArgs
                    {
                        Publisher = "canonical",
                        Offer = "UbuntuServer",
                        Sku = "16.04-LTS",
                        Version = "latest",
                    },
                }, new CustomResourceOptions { DeleteBeforeReplace = true });


            // The public IP address is not allocated until the VM is running, so wait for that
            // resource to create, and then lookup the IP address again to report its public IP.
            var ipAddress = Output
                .Tuple<string, string, string>(vm.Id, publicIp.Name, resourceGroup.Name)
                .Apply<string>(async t => {
                    (_, string name, string resourceGroupName) = t;
                    var ip = await Pulumi.Azure.Network.Invokes.GetPublicIP(new GetPublicIPArgs{ Name = name, ResourceGroupName = resourceGroupName });
                    return ip.IpAddress;
                });

            return new Dictionary<string, object>
            {
                { "ipAddress",  ipAddress }
            };
        });
    }
}