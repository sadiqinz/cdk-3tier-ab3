using Amazon.CDK;
using Constructs;
using Amazon.CDK.AWS.ElasticLoadBalancingV2;
using Amazon.CDK.AWS.AutoScaling;
using Amazon.CDK.AWS.CertificateManager;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.RDS;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.ElastiCache;
using Amazon.CDK.AWS.CloudFront;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudFront.Origins;
using System.IO;
using System;
using System.Text;
using System.Collections.Generic;


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
                        
            //Create a load balancer without any targets     
            ApplicationLoadBalancer lb = new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancer (this, "ExtLoadBalancer", new ApplicationLoadBalancerProps {
                    Vpc = baseNetwork.abVpc,                    
                    InternetFacing = true, 
            });           
            
            string[] userdata = System.IO.File.ReadAllLines("./src/Octankwebapp/lib/userdatafile.txt");

            //Create security group to be used for instances
            SecurityGroup instanceSG = new SecurityGroup(this, "instancesg", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });
            //Allow access from Load Balancer
            instanceSG.Connections.AllowFrom(lb.Connections, Port.Tcp(80));

            //Create a Launch Template
            UserData initData = UserData.ForLinux();
            initData.AddCommands(userdata);
            LaunchTemplate webservertemplate = new LaunchTemplate(this, "webservertemplate", new LaunchTemplateProps{
                UserData = initData,
                InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.MICRO),
                MachineImage = new AmazonLinuxImage(new AmazonLinuxImageProps {
                    Generation = AmazonLinuxGeneration.AMAZON_LINUX_2
                }),
                Role = webInstanceRole,
                SecurityGroup = instanceSG,     
                KeyName = "tmpInstanceKey"
            });

            //Create an AutoScalingGroup
            AutoScalingGroup asg = new Amazon.CDK.AWS.AutoScaling.AutoScalingGroup(this, "ExtAsg", new AutoScalingGroupProps{
                Cooldown = Duration.Seconds(20),
                Vpc = baseNetwork.abVpc,
                LaunchTemplate = webservertemplate,
                VpcSubnets = new SubnetSelection {
                    SubnetGroupName = "webtier"
                },                
                MinCapacity = 2,
                MaxCapacity = 6                
            });

            //Add Scaling policy based on Metric
            //Create a new StepScalingPolicy            
            asg.ScaleOnMetric("ScaleToALBConnections", new BasicStepScalingPolicyProps{
                Metric = lb.MetricActiveConnectionCount(new MetricOptions { Period = Duration.Minutes(1) }),
                ScalingSteps = new [] { new ScalingInterval { Upper = 40, Change = -1 }, new ScalingInterval {Lower = 50, Change = +2}, new ScalingInterval { Lower = 100, Change = +3 }},
                AdjustmentType = AdjustmentType.CHANGE_IN_CAPACITY
            });


            // Target group with duration-based stickiness with load-balancer generated cookie
            ApplicationTargetGroup tg1 = new ApplicationTargetGroup(this, "WebTG1", new ApplicationTargetGroupProps {
                TargetType = TargetType.INSTANCE,
                Port = 80,
                StickinessCookieDuration = Duration.Minutes(5),
                DeregistrationDelay = Duration.Seconds(5),
                Vpc = baseNetwork.abVpc,
                HealthCheck = new Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck { Path = "/index.php", Interval = Duration.Seconds(15) }
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

            //Create a CloudFront Distribution and point to ALB
            new Distribution(this, "octankdistribution", new DistributionProps{
                DefaultBehavior = new BehaviorOptions { Origin = new LoadBalancerV2Origin(lb), OriginRequestPolicy = OriginRequestPolicy.ALL_VIEWER, ViewerProtocolPolicy = ViewerProtocolPolicy.REDIRECT_TO_HTTPS, AllowedMethods = AllowedMethods.ALLOW_ALL },
                DomainNames = new [] { "www.octankfootwear.cloud" },
                Certificate = acmCert

            });
            
            //Create Elasticache Security group
            SecurityGroup redissecGroup = new SecurityGroup(this, "redissecuritygroup", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });

            var baseVPC = baseNetwork.abVpc;
            ISelectedSubnets selection = baseVPC.SelectSubnets(new SubnetSelection {
                SubnetGroupName = "dbtier"
            });

            SubnetSelection dbsubnets = new SubnetSelection {
                SubnetGroupName = "dbtier"
            };
                        
            //Allow access from Webserver to the Cluster
            redissecGroup.Connections.AllowFrom(Peer.SecurityGroupId(instanceSG.SecurityGroupId), Port.Tcp(11211));
            //Create Elasticache SubnetGroup
            CfnSubnetGroup cfnCacheSubnetGroup = new CfnSubnetGroup(this, "MyCfnSubnetGroup", new CfnSubnetGroupProps {
                Description = "CacheSubnetGroup",
                SubnetIds = selection.SubnetIds
            });
                       

            //Create DB Security group
            SecurityGroup dbsecGroup = new SecurityGroup(this, "dbsecuritygroup", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });

            //Create Aurora Serverless cluster
            ServerlessCluster dbCluster = new ServerlessCluster(this, "AuroraServerlessCluster", new ServerlessClusterProps{
                Engine = DatabaseClusterEngine.AURORA_MYSQL,
                Vpc = baseNetwork.abVpc,  
                VpcSubnets = new SubnetSelection{
                    SubnetGroupName = "dbtier"
                },
                SecurityGroups = new [] {dbsecGroup}, 
                EnableDataApi = true,
                DefaultDatabaseName = "shoes_db",
                Scaling = new ServerlessScalingOptions {
                    AutoPause = Duration.Minutes(30),  // default is to pause after 5 minutes of idle time
                    MinCapacity = AuroraCapacityUnit.ACU_1,  // default is 2 Aurora capacity units (ACUs)
                    MaxCapacity = AuroraCapacityUnit.ACU_8,
                    
                },
                Credentials = Credentials.FromGeneratedSecret("proddbuser")
            });

            dbCluster.Secret.GrantRead(webInstanceRole);
            dbCluster.Connections.AllowFrom(Peer.SecurityGroupId(instanceSG.SecurityGroupId), Port.Tcp(3306));  
            
            //Create Autoscaling Group and a Load Balancer for Applicaiton Tier
            //Create security group to be used for instances
            SecurityGroup appInstanceSG = new SecurityGroup(this, "appinstancesg", new SecurityGroupProps{
                Vpc = baseNetwork.abVpc
            });

            //Create an AutoScalingGroup
            AutoScalingGroup intasg = new Amazon.CDK.AWS.AutoScaling.AutoScalingGroup(this, "IntAsg", new AutoScalingGroupProps{
                Cooldown = Duration.Seconds(20),
                Vpc = baseNetwork.abVpc,
                VpcSubnets = new SubnetSelection {
                    SubnetGroupName = "apptier"
                },                
                InstanceType = InstanceType.Of(InstanceClass.BURSTABLE3, InstanceSize.MICRO),
                MachineImage = new AmazonLinuxImage(new AmazonLinuxImageProps {
                    Generation = AmazonLinuxGeneration.AMAZON_LINUX_2
                }),
                Role = webInstanceRole,
                SecurityGroup = appInstanceSG,
                MinCapacity = 2,
                MaxCapacity = 6,
                KeyName = "tmpInstanceKey"
            });
                        
            //Create a load balancer without any targets     
            ApplicationLoadBalancer applb = new Amazon.CDK.AWS.ElasticLoadBalancingV2.ApplicationLoadBalancer (this, "IntLoadBalancer", new ApplicationLoadBalancerProps {
                    Vpc = baseNetwork.abVpc,
                    VpcSubnets = new SubnetSelection {
                        SubnetGroupName = "webtier"
                    },
                    InternetFacing = false
            });

            
            

            // Target group with duration-based stickiness with load-balancer generated cookie
            ApplicationTargetGroup inttg1 = new ApplicationTargetGroup(this, "AppTG1", new ApplicationTargetGroupProps {
                TargetType = TargetType.INSTANCE,
                Port = 80,
                StickinessCookieDuration = Duration.Minutes(5),
                Vpc = baseNetwork.abVpc,
                DeregistrationDelay = Duration.Seconds(5)             
            });        

            //Add a default listener to this internallb
            ApplicationListener intlistener = applb.AddListener("IntListenerHttp", new BaseApplicationListenerProps {
                Port = 80,
                Open = true,
                DefaultAction = ListenerAction.Forward(new IApplicationTargetGroup[] {inttg1})
            
            });   

            //Add TargetGroup to listener
            inttg1.RegisterListener(intlistener);
            
            // //Attache ASG to load balancer
            intasg.AttachToApplicationTargetGroup(inttg1);

            // //Setup ASG's scaling properties
            intasg.ScaleOnRequestCount("ReasonableLoad", new RequestCountScalingProps{
                 TargetRequestsPerMinute =60
             });

            //Add User Data to Interal App Server    
            string[] appuserdata = System.IO.File.ReadAllLines("./src/Octankwebapp/lib/appuserdata.txt");
            intasg.AddUserData(appuserdata);

            //Output required service values
            //Output DBSecret
            new CfnOutput(this, "DBSecret", new CfnOutputProps{
                Value = dbCluster.Secret.SecretFullArn
            });   

            //Output ALB address
            new CfnOutput(this, "ALBAddress", new CfnOutputProps{
                Value = lb.LoadBalancerDnsName
            });

            //Internal ALB address
            new CfnOutput(this, "IntALBAddress", new CfnOutputProps{
                Value = applb.LoadBalancerDnsName
            });
        }         
    }
}

