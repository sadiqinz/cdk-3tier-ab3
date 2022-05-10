using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;


namespace Octankwebapp
{
    public class NetworkSetup : Construct
    {
        public Vpc abVpc { get; }
        public NetworkSetup(Construct scope, string id, IStackProps props = null) : base(scope, id)
        {
            //Need to create the actual Network here
            abVpc = new Vpc(this, "prodVPC", new VpcProps{
                Cidr = "10.1.0.0/16",
                NatGateways = 1,
                SubnetConfiguration = new [] { new SubnetConfiguration {
                    CidrMask = 24,
                    Name = "ingress", 
                    SubnetType = SubnetType.PUBLIC
                }, new SubnetConfiguration {
                    CidrMask =24,
                    Name = "webtier",
                    SubnetType = SubnetType.PRIVATE_WITH_NAT
                }, new SubnetConfiguration {
                    CidrMask =24,
                    Name = "apptier",
                    SubnetType = SubnetType.PRIVATE_WITH_NAT
                }, new SubnetConfiguration {
                    CidrMask =24,
                    Name = "dbtier",
                    SubnetType = SubnetType.PRIVATE_ISOLATED
                } }
            });      

            //Create Code Deploy VPC endpoint
            abVpc.AddInterfaceEndpoint("CodeDeployEndPoint", new InterfaceVpcEndpointOptions{
                Service = InterfaceVpcEndpointAwsService.CODEBUILD
            });
        }
    }
}

