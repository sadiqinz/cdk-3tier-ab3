using Amazon.CDK;
using Constructs;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.AutoScaling;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudFront.Origins;
using System.IO;
using System;
using System.Text;


namespace Octankwebapp
{
    public class OctankwebappStack : Stack
    {
        
        internal OctankwebappStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // Create certificate listener
            ListenerCertificate mylistener = new ListenerCertificate("arn:aws:acm:ap-southeast-2:808754908315:certificate/bd7c3e8d-4672-4967-8382-146b8ef15fb0");
            // Import ACM certificate
            ICertificate acmCert = Certificate.FromCertificateArn(this, "ACM Certificate", "arn:aws:acm:us-east-1:808754908315:certificate/59d88363-4b4f-46a8-bb74-61b499f748a3");

            // S3 bucket with existing artifacts - This would be created outside the stack
            var artifactBucket = Bucket.FromBucketName(this, "ArtifactBucket", "octankwebapp-artifacts-bucket");

            // S3 bucket for CodePipeline Code
            var codePipelineBucket = Bucket.FromBucketName(this, "CodePipeLineBucket", "octankwebapp-codepipeline-shoes-bucket");
            
            
            // IAM role for our Web instance
            var webInstanceRole = new Role(this, "instancerole", new RoleProps{
                AssumedBy = new ServicePrincipal("ec2.amazonaws.com"),
                ManagedPolicies = new [] {ManagedPolicy.FromAwsManagedPolicyName("CloudWatchLogsFullAccess"), ManagedPolicy.FromAwsManagedPolicyName("AmazonSSMManagedInstanceCore"), ManagedPolicy.FromAwsManagedPolicyName("CloudWatchAgentServerPolicy")}
            });
            
            // Grant access to S3 artifact bucket
            artifactBucket.GrantRead(webInstanceRole);
            codePipelineBucket.GrantRead(webInstanceRole);
            // Grant access to Codebuild Buckets
            webInstanceRole.AttachInlinePolicy(new Policy(this, "userpool-policy", new PolicyProps {
                Statements = new [] { new PolicyStatement(new PolicyStatementProps {
                    Actions = new [] { "s3:Get*", "s3:List*" },
                    Resources = new [] { "arn:aws:s3:::aws-codedeploy-ap-southeast-2/*" }
                }) }
            }));

            //Create base VPC
            var baseNetwork = new NetworkSetup(this, "NetworkSetupAU");                        
            
            //Create security group to be used for instances
            SecurityGroup instanceSG = new SecurityGroup(this, "instancesg", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });
            //Create an AutoScalingGroup
            AutoScalingGroup asg = new Amazon.CDK.AWS.AutoScaling.AutoScalingGroup(this, "myasg", new AutoScalingGroupProps{
                Vpc = baseNetwork.abVpc,
                InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.MICRO),
                MachineImage = new AmazonLinuxImage(new AmazonLinuxImageProps {
                    Generation = AmazonLinuxGeneration.AMAZON_LINUX_2
                }),
                Role = webInstanceRole,
                SecurityGroup = instanceSG,
                MinCapacity = 1,
                MaxCapacity = 4,
                KeyName = "tmpInstanceKey"
            });

            //Create a load balancer without any targets     
            ApplicationLoadBalancer lb = new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancer (this, "ExtLoadBalancer", new ApplicationLoadBalancerProps {
                    Vpc = baseNetwork.abVpc,
                    InternetFacing = true, 
            });

            //Create a CloudFront Distribution and point to ALB
            new Distribution(this, "octankdistribution", new DistributionProps{
                DefaultBehavior = new BehaviorOptions { Origin = new LoadBalancerV2Origin(lb), OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER },
                DomainNames = new [] { "www.octankfootwear.cloud" },
                Certificate = acmCert

            });
            
            // Target group with duration-based stickiness with load-balancer generated cookie
            ApplicationTargetGroup tg1 = new ApplicationTargetGroup(this, "TG1", new ApplicationTargetGroupProps {
                TargetType = TargetType.INSTANCE,
                Port = 80,
                StickinessCookieDuration = Duration.Minutes(5),
                Vpc = baseNetwork.abVpc
            });
            //Add a default listener to this lb
            ApplicationListener listener = lb.AddListener("ListenerHttp", new BaseApplicationListenerProps {
                Port = 443,
                Open = true,
                Certificates = new [] {mylistener}
            });

            //Add TargetGroup to listener
            tg1.RegisterListener(listener);
            listener.AddAction("listerntg", new AddApplicationActionProps {
                Action = ListenerAction.Forward(new IApplicationTargetGroup[] {tg1})
            });
            //Attache ASG to load balancer
            asg.AttachToApplicationTargetGroup(tg1);
            //Setup ASG's scaling properties
            asg.ScaleOnRequestCount("ReasonableLoad", new RequestCountScalingProps{
                TargetRequestsPerMinute =60
            });

            //Add UserData
            string[] userdata = System.IO.File.ReadAllLines("./src/Octankwebapp/lib/userdatafile.txt");

            //Console.WriteLine("[{0}]", string.Join(", ", userdata));

            //{"sudo yum update -y", "sudo amazon-linux-extras install -y lamp-mariadb10.2-php7.2 php7.2", "cat /etc/system-release", "sudo yum install -y httpd", "sudo systemctl start httpd" ,"sudo systemctl enable httpd", "sudo touch /var/www/html/index.html"};
            asg.AddUserData(userdata);

            //Create DB Security group
            SecurityGroup dbsecGroup = new SecurityGroup(this, "dbsecuritygroup", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });
            //Create required DB
            DatabaseInstance dbInstance = new DatabaseInstance(this, "SingleDBInstnace", new DatabaseInstanceProps{
                Engine = DatabaseInstanceEngine.Mysql(new MySqlInstanceEngineProps{ Version = MysqlEngineVersion.VER_8_0_20}),
                DatabaseName = "shoes_db",
                Vpc = baseNetwork.abVpc,
                VpcSubnets = new SubnetSelection{
                    SubnetType = SubnetType.PRIVATE_WITH_NAT
                },
                SecurityGroups = new [] {dbsecGroup},
                InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.MEDIUM),
                Credentials = Credentials.FromGeneratedSecret("proddbuser")
            });

            //Give access to secrets manager for Instance role
            dbInstance.Secret.GrantRead(webInstanceRole);

            //Allow access from instances to DB
            dbInstance.Connections.AllowFrom(Peer.SecurityGroupId(instanceSG.SecurityGroupId), Port.Tcp(3306));    

            //Output required service values
            //Output DBSecret
            new CfnOutput(this, "DBSecret", new CfnOutputProps{
                Value = dbInstance.Secret.SecretFullArn
            });   

            //Output ALB address
            new CfnOutput(this, "ALBAddress", new CfnOutputProps{
                Value = lb.LoadBalancerDnsName
            });
        }         
    }
}

