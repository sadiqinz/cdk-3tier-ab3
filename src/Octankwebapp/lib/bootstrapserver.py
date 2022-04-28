#! /usr/bin/python3

import sys
import fileinput
import boto3
import base64
import json

from botocore.exceptions import ClientError

db_file = 'dbinfo.inc'

# Function to get details about database
def get_secret():
    # Define global variables to be used outside the function
    
    global DB_SERVER
    global DB_USERNAME
    global DB_PASSWORD
    global DB_DATABASE            
    
    secret_name = "arn:aws:secretsmanager:ap-southeast-2:972552287170:secret:AbtrainingStackSingleDBInst-rBA7Dg2jqdIN-5CiK8r"
    region_name = "ap-southeast-2"

    # Create a Secrets Manager client
    session = boto3.session.Session()
    client = session.client(
        service_name='secretsmanager',
        region_name=region_name
    )

    # In this sample we only handle the specific exceptions for the 'GetSecretValue' API.
    # See https://docs.aws.amazon.com/secretsmanager/latest/apireference/API_GetSecretValue.html
    # We rethrow the exception by default.

    try:
        get_secret_value_response = client.get_secret_value(
            SecretId=secret_name
        )
    except ClientError as e:
        if e.response['Error']['Code'] == 'DecryptionFailureException':
            # Secrets Manager can't decrypt the protected secret text using the provided KMS key.
            # Deal with the exception here, and/or rethrow at your discretion.
            raise e
        elif e.response['Error']['Code'] == 'InternalServiceErrorException':
            # An error occurred on the server side.
            # Deal with the exception here, and/or rethrow at your discretion.
            raise e
        elif e.response['Error']['Code'] == 'InvalidParameterException':
            # You provided an invalid value for a parameter.
            # Deal with the exception here, and/or rethrow at your discretion.
            raise e
        elif e.response['Error']['Code'] == 'InvalidRequestException':
            # You provided a parameter value that is not valid for the current state of the resource.
            # Deal with the exception here, and/or rethrow at your discretion.
            raise e
        elif e.response['Error']['Code'] == 'ResourceNotFoundException':
            # We can't find the resource that you asked for.
            # Deal with the exception here, and/or rethrow at your discretion.
            raise e
    else:
        # Decrypts secret using the associated KMS key.
        # Depending on whether the secret is a string or binary, one of these fields will be populated.
        if 'SecretString' in get_secret_value_response:
            secret = get_secret_value_response['SecretString']
        else:
            decoded_binary_secret = base64.b64decode(get_secret_value_response['SecretBinary'])

	# Get Json payload for secret
    secret_value = json.loads(secret)

	# Get DB parameters
    DB_SERVER = secret_value['host']
    DB_USERNAME = secret_value['username']
    DB_PASSWORD = secret_value['password']
    DB_DATABASE = secret_value['dbInstanceIdentifier']

get_secret()

# ### Test Code ####
# DB_SERVER = 'server'
# DB_USERNAME = 'username'
# DB_PASSWORD = 'password'
# DB_DATABASE = 'dbInstanceIdentifier'


#####

f = open(db_file,'r')
filedata = f.read()
f.close()

filedata = filedata.replace("db_instance_endpoint",DB_SERVER)
filedata = filedata.replace("masteruser",DB_USERNAME)
filedata = filedata.replace("masterpassword",DB_PASSWORD)
filedata = filedata.replace("sample",DB_DATABASE)

f = open(db_file,'w')
f.write(filedata)
f.close()


# Fill in values in DB connections file
# for i, line in enumerate(fileinput.input(db_file, inplace=1)):
#     sys.stdout.write(line.replace('db_instance_endpoint', DB_SERVER))
#     sys.stdout.write(line.replace('masteruser', DB_USERNAME))
#     sys.stdout.write(line.replace('masterpassword', DB_PASSWORD))
#     sys.stdout.write(line.replace('sample', DB_DATABASE))